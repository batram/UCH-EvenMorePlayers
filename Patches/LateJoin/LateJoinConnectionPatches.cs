using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;
using UnityEngine.SceneManagement;

namespace MorePlayers.LateJoin
{
    // Opens the UNet connection gates so a client can join while a level scene is
    // running, defers client readiness until the level scene is loaded, and keeps
    // the vanilla kick/cleanup paths off the late joiner's back.
    //
    // The vanilla flow this modifies (see notes/UCH_LATEJOIN_ANALYSIS.md):
    //  - NetworkLobbyManager.OnServerConnect / OnServerAddPlayer refuse anything
    //    while the active scene is not "TreeHouseLobby".
    //  - With onlineScene == null the client calls ClientScene.Ready +
    //    AddPlayer(0) at connect time, BEFORE loading the level scene the server
    //    announces in MsgType.Scene (39) - scene objects would mis-resolve.
    //  - LobbyManager.DisconnectBrokenClients reclaims connections without a
    //    Lobby/GamePlayer after ~3s; a late joiner needs longer to load a level.
    //  - LobbyManager.OnLobbyClientSceneChanged self-kicks characterless players
    //    in level scenes via LobbyPlayer.CallCmdIShouldNotBeHere.
    public static class LateJoinPatches
    {
        // A session counts as "running" for late-join purposes when the vanilla
        // matchmaking lobby reports non-zero match progress. Progress is set at the
        // level-launch countdown and reset to 0 whenever the party is back in the
        // treehouse, which is exactly the window where vanilla joining works.
        public static bool MatchInProgress()
        {
            try
            {
                MatchmakingLobby lobby = Matchmaker.CurrentMatchmakingLobby;
                return lobby != null && lobby.GetMatchProgress() != 0;
            }
            catch
            {
                return false;
            }
        }

        // Patch 1: accept connections while a level scene is active.
        [HarmonyPatch(typeof(NetworkLobbyManager), nameof(NetworkLobbyManager.OnServerConnect))]
        static class NetworkLobbyManagerOnServerConnectPatch
        {
            static bool Prefix(NetworkLobbyManager __instance, NetworkConnection conn)
            {
                if (!LateJoinState.Enabled)
                {
                    return true;
                }
                if (SceneManager.GetSceneAt(0).name == __instance.lobbyScene)
                {
                    // Vanilla accepts in the lobby scene. If the countdown already
                    // started (progress != 0) the joining client will defer its
                    // AddPlayer, so exempt it from the broken-client cleanup.
                    if (MatchInProgress())
                    {
                        LateJoinState.PendingConnections[conn] = 0f;
                    }
                    return true;
                }
                // Mid-level: reimplement the original without the scene gate.
                if (__instance.numPlayers > __instance.maxPlayers)
                {
                    Debug.LogWarning("[LateJoin] refusing connection " + conn + ": too many players");
                    conn.Disconnect();
                    return false;
                }
                Debug.Log("[LateJoin] accepting mid-game connection " + conn);
                for (int i = 0; i < __instance.lobbySlots.Length; i++)
                {
                    if (__instance.lobbySlots[i])
                    {
                        __instance.lobbySlots[i].SetDirtyBit(1U);
                    }
                }
                LateJoinState.PendingConnections[conn] = 0f;
                __instance.OnLobbyServerConnect(conn);
                return false;
            }
        }

        // Patch 2: create a LobbyPlayer for AddPlayer requests arriving mid-level.
        [HarmonyPatch(typeof(NetworkLobbyManager), nameof(NetworkLobbyManager.OnServerAddPlayer))]
        static class NetworkLobbyManagerOnServerAddPlayerPatch
        {
            static bool Prefix(NetworkLobbyManager __instance, NetworkConnection conn, short playerControllerId)
            {
                if (!LateJoinState.Enabled)
                {
                    return true;
                }
                if (SceneManager.GetSceneAt(0).name == __instance.lobbyScene)
                {
                    return true;
                }
                // Reimplementation of NetworkLobbyManager.OnServerAddPlayer minus
                // the scene early-out.
                int validControllers = 0;
                for (int i = 0; i < conn.playerControllers.Count; i++)
                {
                    if (conn.playerControllers[i].IsValid)
                    {
                        validControllers++;
                    }
                }
                if (validControllers >= __instance.maxPlayersPerConnection)
                {
                    Debug.LogWarning("[LateJoin] no more players for connection " + conn);
                    conn.Send(45, new EmptyMessage());
                    return false;
                }
                byte slot = __instance.FindSlot();
                if (slot == byte.MaxValue)
                {
                    Debug.LogWarning("[LateJoin] no free lobby slot for " + conn);
                    conn.Send(45, new EmptyMessage());
                    return false;
                }
                GameObject playerObject = __instance.OnLobbyServerCreateLobbyPlayer(conn, playerControllerId);
                if (playerObject == null)
                {
                    playerObject = Object.Instantiate<GameObject>(__instance.lobbyPlayerPrefab.gameObject, Vector3.zero, Quaternion.identity);
                }
                NetworkLobbyPlayer lobbyPlayer = playerObject.GetComponent<NetworkLobbyPlayer>();
                lobbyPlayer.slot = slot;
                __instance.lobbySlots[(int)slot] = lobbyPlayer;
                LobbyPlayer uchLobbyPlayer = playerObject.GetComponent<LobbyPlayer>();
                if (uchLobbyPlayer != null)
                {
                    // Protect the joiner from "no character picked" purges until it
                    // is activated or handed to the spectator system.
                    LateJoinState.ProtectedNumbers.Add(uchLobbyPlayer.networkNumber);
                    Debug.Log("[LateJoin] created mid-game LobbyPlayer networkNumber=" + uchLobbyPlayer.networkNumber);
                }
                NetworkServer.AddPlayerForConnection(conn, playerObject, playerControllerId);
                return false;
            }
        }

        // Direct-IP has no pre-connect match metadata. Its first Ready arrives
        // while the client still has treehouse scene objects. Hold it so the
        // server does not send active-level spawns until ClientChangeScene has
        // loaded the announced scene and the client sends Ready again.
        [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.OnServerReady))]
        static class DeferInitialDirectIpReadyPatch
        {
            static bool Prefix(NetworkConnection conn)
            {
                if (!LateJoinState.Enabled || conn == null
                    || !LateJoinState.PendingConnections.ContainsKey(conn))
                {
                    return true;
                }

                if (!LateJoinState.DeferredReadyConnections.Contains(conn))
                {
                    LateJoinState.DeferredReadyConnections.Add(conn);
                    Debug.Log("[LateJoin] deferring initial Ready until active level is loaded for " + conn);
                    return false;
                }

                LateJoinState.DeferredReadyConnections.Remove(conn);
                LateJoinState.PendingConnections.Remove(conn);
                // AddPlayerForConnection can mark the connection ready again
                // between the deferred and post-load Ready messages. Its old
                // observer set represents pre-load deliveries, so clear it
                // server-side without sending a NotReady scene message back to
                // the now-correct client scene. Base OnServerReady rebuilds it.
                if (conn.isReady)
                {
                    conn.isReady = false;
                    conn.RemoveObservers();
                }
                // GameControl's observer check excludes a characterless joiner
                // that is not in PlayerQueue yet. Send it first: its client-side
                // initialization registers the level gameplay prefabs needed by
                // the remaining spawn batch that base OnServerReady emits.
                GameControl gameControl = LobbyManager.instance != null
                    ? LobbyManager.instance.CurrentGameController
                    : null;
                NetworkIdentity gameIdentity = gameControl != null
                    ? gameControl.GetComponent<NetworkIdentity>()
                    : null;
                if (gameIdentity != null)
                {
                    NetworkServer.instance.SendSpawnMessage(gameIdentity, conn);
                    gameIdentity.AddObserver(conn);
                    Debug.Log("[LateJoin] sent GameControl before post-load spawn batch to " + conn);
                }
                Debug.Log("[LateJoin] accepting post-load Ready for " + conn);
                return true;
            }
        }

        // Patch 3: keep DisconnectBrokenClients away from still-loading joiners.
        [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.DisconnectBrokenClients))]
        static class DisconnectBrokenClientsPatch
        {
            static readonly List<NetworkConnection> scratch = new List<NetworkConnection>();

            static void Prefix(LobbyManager __instance)
            {
                if (!LateJoinState.Enabled || LateJoinState.PendingConnections.Count == 0)
                {
                    return;
                }
                scratch.Clear();
                scratch.AddRange(LateJoinState.PendingConnections.Keys);
                foreach (NetworkConnection conn in scratch)
                {
                    if (conn == null)
                    {
                        LateJoinState.PendingConnections.Remove(conn);
                        continue;
                    }
                    bool hasPlayer = false;
                    if (conn.playerControllers != null)
                    {
                        foreach (PlayerController controller in conn.playerControllers)
                        {
                            if (controller.gameObject != null &&
                                (controller.gameObject.GetComponent<LobbyPlayer>() != null ||
                                 controller.gameObject.GetComponent<GamePlayer>() != null))
                            {
                                hasPlayer = true;
                                break;
                            }
                        }
                    }
                    float waited = LateJoinState.PendingConnections[conn] + Time.unscaledDeltaTime;
                    if ((hasPlayer && !LateJoinState.DeferredReadyConnections.Contains(conn))
                        || waited > LateJoinState.LateJoinTimeoutSeconds)
                    {
                        // Either done (LobbyPlayer arrived) or out of patience:
                        // return the connection to vanilla bookkeeping.
                        LateJoinState.PendingConnections.Remove(conn);
                        LateJoinState.DeferredReadyConnections.Remove(conn);
                        continue;
                    }
                    LateJoinState.PendingConnections[conn] = waited;
                    __instance.connectionLifetimes.Remove(conn);
                    __instance.brokenClientConnections.Remove(conn);
                }
            }
        }

        // Patch 4: on a late-joining client, skip the premature
        // ClientScene.Ready/AddPlayer at connect time. The server's scene message
        // (39) triggers the level load, after which NetworkManager's
        // OnClientSceneChanged performs Ready + AddPlayer with the scene in place.
        [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.OnClientConnect))]
        static class LobbyManagerOnClientConnectPatch
        {
            static bool Prefix(LobbyManager __instance, NetworkConnection conn)
            {
                if (!LateJoinState.Enabled || NetworkServer.active)
                {
                    return true; // host's loopback client uses the vanilla path
                }
                if (!MatchInProgress())
                {
                    return true;
                }
                LateJoinState.ClientJoiningLate = true;
                Debug.Log("[LateJoin] joining a running match - deferring ClientScene.Ready until the announced scene is loaded (conn: " + conn + ")");
                // Replicate NetworkLobbyManager.OnClientConnect's lobby bookkeeping
                // but skip NetworkManager.OnClientConnect (Ready + AddPlayer).
                __instance.OnLobbyClientConnect(conn);
                __instance.CallOnClientEnterLobby();
                return false;
            }
        }

        // Direct-IP NetTest joins do not have matchmaking progress metadata at
        // connect time. The authoritative signal is the server's scene message:
        // ClientChangeScene is invoked from MsgType.Scene (39), before the level
        // finishes loading and before its local LobbyPlayer starts. Arm the late
        // join handshake from that non-treehouse destination as well.
        [HarmonyPatch(typeof(NetworkManager), "ClientChangeScene")]
        static class DirectIpSceneTransitionPatch
        {
            static void Prefix(NetworkManager __instance, string newSceneName)
            {
                NetworkLobbyManager lobbyManager = __instance as NetworkLobbyManager;
                if (!LateJoinState.Enabled || NetworkServer.active
                    || lobbyManager == null || __instance.client == null
                    || !__instance.client.isConnected
                    || string.IsNullOrEmpty(newSceneName)
                    || newSceneName == lobbyManager.lobbyScene
                    || newSceneName == __instance.offlineScene)
                {
                    return;
                }

                // A client already participating in the treehouse has a picked,
                // active local LobbyPlayer when the host launches a level. That
                // is a normal synchronized scene transition, not a late join.
                // A direct-IP client created mid-match reaches this point with
                // its just-created local LobbyPlayer still INACTIVE/NONE.
                if (LobbyManager.instance != null)
                {
                    foreach (NetworkLobbyPlayer slot in LobbyManager.instance.lobbySlots)
                    {
                        LobbyPlayer local = slot as LobbyPlayer;
                        if (local != null && local.isLocalPlayer
                            && (local.PlayerStatus != LobbyPlayer.Status.INACTIVE
                                || local.PickedAnimal != Character.Animals.NONE))
                        {
                            return;
                        }
                    }
                }

                if (!LateJoinState.ClientJoiningLate)
                {
                    LateJoinState.ClientJoiningLate = true;
                    Debug.Log("[LateJoin] server directed client to active level "
                        + newSceneName + " - arming direct-IP late-join handshake");
                }
                // OnClientConnect may already have sent Ready in direct-IP mode.
                // Make FinishLoadScene send a second Ready after the correct
                // scene is present; the host deliberately deferred the first.
                ClientScene.SetNotReady();
                LateJoinWelcome.SendHelloForExistingLocalPlayer();
            }
        }

        // Patch 5 (client side): a late joiner in a level scene without a picked
        // character must not report itself for a kick.
        [HarmonyPatch(typeof(LobbyPlayer), nameof(LobbyPlayer.CallCmdIShouldNotBeHere))]
        static class CallCmdIShouldNotBeHerePatch
        {
            static bool Prefix(LobbyPlayer __instance)
            {
                if (LateJoinState.Enabled && LateJoinState.ClientJoiningLate && !LateJoinState.ClientIntegrated)
                {
                    Debug.Log("[LateJoin] suppressing self-kick report for late joiner " + __instance.networkNumber);
                    return false;
                }
                return true;
            }
        }

        // Patch 6 (host side): ignore stray "I should not be here" reports from
        // protected late joiners (belt and braces for peers with stale state).
        [HarmonyPatch(typeof(LobbyPlayer), nameof(LobbyPlayer.CmdIShouldNotBeHere))]
        static class CmdIShouldNotBeHerePatch
        {
            static bool Prefix(LobbyPlayer __instance)
            {
                if (LateJoinState.Enabled && LateJoinState.IsProtected(__instance.networkNumber))
                {
                    Debug.Log("[LateJoin] ignoring self-kick report from protected late joiner " + __instance.networkNumber);
                    return false;
                }
                return true;
            }
        }

        // Patch 7: NetworkLobbyManager.ServerChangeScene, on returning to the
        // lobby scene, destroys each connection's current player object and
        // re-adds the LobbyPlayer via ReplacePlayerForConnection. For a late
        // joiner whose current player object still IS the LobbyPlayer (no
        // GamePlayer was ever created), that destroys their LobbyPlayer and breaks
        // the client. Reimplementation that skips destroy/replace in that case.
        [HarmonyPatch(typeof(NetworkLobbyManager), nameof(NetworkLobbyManager.ServerChangeScene))]
        static class ServerChangeScenePatch
        {
            static bool Prefix(NetworkLobbyManager __instance, string sceneName)
            {
                if (!LateJoinState.Enabled || sceneName != __instance.lobbyScene)
                {
                    return true; // only the lobby-return branch needs fixing
                }
                if (string.IsNullOrEmpty(sceneName))
                {
                    return true;
                }
                for (int i = 0; i < __instance.lobbySlots.Length; i++)
                {
                    NetworkLobbyPlayer slotPlayer = __instance.lobbySlots[i];
                    if (slotPlayer == null)
                    {
                        continue;
                    }
                    NetworkIdentity identity = slotPlayer.GetComponent<NetworkIdentity>();
                    if (identity.connectionToClient == null)
                    {
                        continue;
                    }
                    PlayerController playerController;
                    if (identity.connectionToClient.GetPlayerController(identity.playerControllerId, out playerController))
                    {
                        if (playerController.gameObject == slotPlayer.gameObject)
                        {
                            // Late joiner without a GamePlayer: the LobbyPlayer is
                            // already the active player object - leave it alone.
                            slotPlayer.readyToBegin = false;
                            Debug.Log("[LateJoin] ServerChangeScene: keeping LobbyPlayer of GamePlayer-less connection " + identity.connectionToClient);
                            continue;
                        }
                        NetworkServer.Destroy(playerController.gameObject);
                    }
                    if (NetworkServer.active)
                    {
                        slotPlayer.readyToBegin = false;
                        NetworkServer.ReplacePlayerForConnection(identity.connectionToClient, slotPlayer.gameObject, identity.playerControllerId);
                    }
                }
                // Replicated body of NetworkManager.ServerChangeScene.
                NetworkServer.SetAllClientsNotReady();
                NetworkManager.networkSceneName = sceneName;
                NetworkManager.s_LoadingSceneAsync = SceneManager.LoadSceneAsync(sceneName);
                NetworkServer.SendToAll(39, new StringMessage(sceneName));
                NetworkManager.s_StartPositionIndex = 0;
                if (NetworkManager.s_StartPositions != null)
                {
                    NetworkManager.s_StartPositions.Clear();
                }
                return false;
            }
        }

        // State cleanup when the session ends on either side.
        [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.OnStopClient))]
        static class OnStopClientResetPatch
        {
            static void Postfix()
            {
                LateJoinState.Reset();
            }
        }

        [HarmonyPatch(typeof(NetworkLobbyManager), nameof(NetworkLobbyManager.OnStopHost))]
        static class OnStopHostResetPatch
        {
            static void Postfix()
            {
                LateJoinState.Reset();
            }
        }
    }
}
