using GameSparks.Api.Responses;
using GameSparks.Core;
using HarmonyLib;
using System;
using System.Collections;
using UCHServices;
using UnityEngine;
using UnityEngine.Events;

namespace MorePlayers
{
    // Steam invites, Steam "Join Game", +connect_lobby, lobby codes and the lobby list all end up in
    // GamesparksMatchmaker.attemptToJoinGamesparksLobby once the backend is authenticated. The Steam
    // join request itself only carries the Steam lobby and friend id, and the Steam lobby only holds
    // "gamesparksID", so the host's version is only known from the backend lobby data.
    //
    // Before the real join we read that lobby data without reserving a slot, switch into the host's
    // mode and only then run the original join. That way everything mode dependent on the join path
    // (reserveSlot in DoGetLobbyData, player limit in CheckHostConnectivity/onLobbyJoined, version
    // check) already runs in the right mode, instead of hot switching halfway through.
    //
    // Lives in its own Harmony instance, so it survives ModMode unpatching the player number patches.
    // Also guards a stock cold start race in JoinLobby, see JoinLobbyPrefix, and joins into P2P
    // lobbies, see LobbyRegionPostfix.
    static class JoinPreflight
    {
        const float TimeoutSeconds = 8f;

        static bool bypass;
        static int generation;

        const float BackendWaitSeconds = 60f;

        public static void Patch()
        {
            var harmony = new Harmony("EvenMorePlayers.JoinPreflight");
            var original = AccessTools.Method(typeof(GamesparksMatchmaker), nameof(GamesparksMatchmaker.attemptToJoinGamesparksLobby));
            var prefix = AccessTools.Method(typeof(JoinPreflight), nameof(Prefix));
            harmony.Patch(original, prefix: new HarmonyMethod(prefix));

            var join = AccessTools.Method(typeof(GamesparksMatchmaker), nameof(GamesparksMatchmaker.JoinLobby));
            var joinPrefix = AccessTools.Method(typeof(JoinPreflight), nameof(JoinLobbyPrefix));
            harmony.Patch(join, prefix: new HarmonyMethod(joinPrefix));

            var region = AccessTools.Method(typeof(GamesparksMatchmakingLobby), nameof(GamesparksMatchmakingLobby.GetLobbyRegion));
            var regionPostfix = AccessTools.Method(typeof(JoinPreflight), nameof(LobbyRegionPostfix));
            harmony.Patch(region, postfix: new HarmonyMethod(regionPostfix));
        }

        // Stock bug: a "Host P2P" lobby never gets a relay region (only CreateMatch sets it), so
        // GetLobbyRegion returns null. gamesparksMatchJoined copies it into GameSettings.SelectedRegion
        // and requests a relay server anyway, which throws on SelectedRegion.queryAddress: every
        // invite/code/browser join into a P2P lobby fails, and the null region then makes the
        // RegionCycle button throw every frame. Stock already falls back to the first region for
        // invalid lobbies, extend that to the missing key.
        static void LobbyRegionPostfix(ref AvailableRegion __result)
        {
            if (__result == null)
            {
                var settings = GameSettings.GetInstance();
                if (settings.SelectedRegion != null || settings.ClosestRegion != null || RelayConstants.AVAILABLE_REGIONS.Count > 0)
                {
                    __result = settings.SelectedRegion ?? settings.ClosestRegion ?? RelayConstants.AVAILABLE_REGIONS[0];
                }
            }
        }

        static bool holdBypass;

        // The relay region list is downloaded after MainMenu starts (TabletMainMenuHome ->
        // RelayConstants.PopulateAvailableRegions). Until then GetLobbyRegion cannot resolve the
        // host's region id, so a join started that early asks the wrong region for the relay server.
        static bool BackendReady()
        {
            return GameSparksManager.Instance != null && RelayConstants.AVAILABLE_REGIONS.Count > 0;
        }

        // Stock races: accepting a Steam invite while the game is closed starts it with +connect_lobby,
        // and Steam can deliver the lobby enter while still in the Init scene. GameSparksManager only
        // exists from MainMenu on, so GamesparksMatchmaker.JoinLobby throws on GameSparksManager.Instance
        // and the join is lost; slightly later the region list is still empty (see BackendReady).
        // Hold the join until both are there.
        static bool JoinLobbyPrefix(GamesparksMatchmaker __instance, string lobbyID, bool useCode, UnityAction<bool> callback)
        {
            if (holdBypass || BackendReady())
            {
                return true;
            }
            Debug.Log("[MorePlayersMod] backend not up yet, holding join of " + lobbyID);
            __instance.StartCoroutine(JoinWhenBackendReady(__instance, lobbyID, useCode, callback));
            return false;
        }

        static IEnumerator JoinWhenBackendReady(GamesparksMatchmaker matchmaker, string lobbyID, bool useCode, UnityAction<bool> callback)
        {
            // wall clock, frame deltas are clamped while scenes load
            float start = Time.realtimeSinceStartup;
            float waited = 0f;
            while (!BackendReady() && waited < BackendWaitSeconds)
            {
                yield return null;
                waited = Time.realtimeSinceStartup - start;
            }
            if (GameSparksManager.Instance == null)
            {
                Debug.Log("[MorePlayersMod] backend did not come up, dropping join of " + lobbyID);
                yield break;
            }
            Debug.Log("[MorePlayersMod] backend up after " + waited.ToString("0.0") + "s ("
                + RelayConstants.AVAILABLE_REGIONS.Count + " regions), joining " + lobbyID);
            // without regions after the wait the join still runs, stock behaviour from here on
            holdBypass = true;
            try
            {
                matchmaker.JoinLobby(lobbyID, useCode, callback);
            }
            finally
            {
                holdBypass = false;
            }
        }

        static bool Prefix(GamesparksMatchmaker __instance, string lobbyID, bool useCode, UnityAction<bool> callback)
        {
            if (bypass)
            {
                return true;
            }

            int gen = ++generation;
            Action<string> proceed = delegate (string lobbyVersion)
            {
                // first of response/timeout wins, a newer join supersedes this one
                if (gen != generation)
                {
                    return;
                }
                generation++;

                if (!__instance.joiningLobby)
                {
                    Debug.Log("[MorePlayersMod] join of " + lobbyID + " was cancelled during version preflight");
                    return;
                }

                ModMode.FollowHostVersion(lobbyVersion);

                bypass = true;
                try
                {
                    __instance.attemptToJoinGamesparksLobby(lobbyID, useCode, callback);
                }
                finally
                {
                    bypass = false;
                }
            };

            Debug.Log("[MorePlayersMod] version preflight for lobby " + lobbyID + (useCode ? " (code)" : ""));
            // reserveSlot: false, only reading the lobby data
            GameSparksManager.Instance.CreateQuery(false).GetLobbyData(lobbyID, useCode, delegate (LogEventResponse response)
            {
                proceed(ReadLobbyVersion(response));
            }, false);
            // GameSparksQuery drops the callback on timeout, don't let that stall the join
            __instance.StartCoroutine(Timeout(gen, proceed));
            return false;
        }

        static IEnumerator Timeout(int gen, Action<string> proceed)
        {
            yield return new WaitForSecondsRealtime(TimeoutSeconds);
            if (gen == generation)
            {
                Debug.Log("[MorePlayersMod] version preflight timed out");
                proceed(null);
            }
        }

        static string ReadLobbyVersion(LogEventResponse response)
        {
            if (response == null || response.HasErrors || response.ScriptData == null)
            {
                // e.g. unknown code or lobby, the real join reports it
                return null;
            }
            GSData match = response.ScriptData.GetGSData("match");
            return match?.GetString(MatchmakingLobby.data_version);
        }
    }
}
