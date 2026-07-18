using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace MorePlayers.LateJoin
{
    // M5: keep the hosted lobby visible and joinable in the lobby browser while
    // a game is running (vanilla hides it when leaving the treehouse,
    // LevelSelectController.cs:705, and the AFK paths toggle joinability,
    // UnityMatchmaker.cs:212-213/472-474).
    //
    // The browser already lists in-progress lobbies sorted last
    // (SteamLobbySearchList.cs:143-160); joiners detect the running match via
    // GetMatchProgress() != 0, so no custom lobby data is needed.
    public static class LateJoinBrowser
    {
        static bool KeepVisibleActive
        {
            get
            {
                return LateJoinState.Enabled
                    && MorePlayersMod.lateJoinKeepVisible != null
                    && MorePlayersMod.lateJoinKeepVisible.Value
                    && NetworkServer.active;
            }
        }

        // Swallow the "hide lobby" call the host makes when the party leaves the
        // treehouse. Re-showing (visible == true) always passes through.
        [HarmonyPatch(typeof(MatchmakingLobby), nameof(MatchmakingLobby.SetLobbyVisible))]
        static class SetLobbyVisiblePatch
        {
            static bool Prefix(bool visible)
            {
                if (!visible && KeepVisibleActive)
                {
                    Debug.Log("[LateJoin] keeping lobby visible in the browser (SetLobbyVisible(false) suppressed)");
                    return false;
                }
                return true;
            }
        }

        // Force the Steam lobby to stay joinable (covers the AFK kick paths that
        // temporarily close the lobby).
        [HarmonyPatch(typeof(SteamMatchmakingLobby), nameof(SteamMatchmakingLobby.SetLobbyJoinable))]
        static class SetLobbyJoinablePatch
        {
            static void Prefix(ref bool joinable)
            {
                if (!joinable && KeepVisibleActive)
                {
                    Debug.Log("[LateJoin] keeping lobby joinable (SetLobbyJoinable(false) overridden)");
                    joinable = true;
                }
            }
        }
    }
}
