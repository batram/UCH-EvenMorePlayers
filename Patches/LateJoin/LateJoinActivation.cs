using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace MorePlayers.LateJoin
{
    // Protects an undecided/spectating late joiner from the vanilla "no character
    // picked" removal paths, and (M4) activates a joiner who picked a character
    // into the running game at the next build phase.
    //
    // Vanilla purge paths that target unpicked players when a round starts:
    //  - GameControl.SetupStart host branch: LobbyPlayer.RemovePlayer() calls
    //    (these self-remove only on the machine where the player is local).
    //  - GameControl.SetupStart client branch: direct
    //    ClientScene.RemovePlayer(playerControllerId) for local players whose
    //    tracker entry has no GameNetID.
    //  - LevelSelectController.LaunchLevel: LobbyPlayer.RemovePlayer() for
    //    unpicked players before the scene change.
    public static class LateJoinActivation
    {
        static bool LocallyProtected(LobbyPlayer lobbyPlayer)
        {
            if (lobbyPlayer == null)
            {
                return false;
            }
            if (LateJoinState.IsProtected(lobbyPlayer.networkNumber))
            {
                return true;
            }
            // The joiner protects itself even before the protection set has been
            // relayed: its own unintegrated late-join state is authoritative.
            return LateJoinState.ClientJoiningLate && !LateJoinState.ClientIntegrated && lobbyPlayer.IsLocalPlayer;
        }

        // Purge guard A: NetworkLobbyPlayer.RemovePlayer (self-removal of local
        // players; used by SetupStart's host branch and LaunchLevel).
        [HarmonyPatch(typeof(NetworkLobbyPlayer), nameof(NetworkLobbyPlayer.RemovePlayer))]
        static class RemovePlayerGuard
        {
            static bool Prefix(NetworkLobbyPlayer __instance)
            {
                if (!LateJoinState.Enabled)
                {
                    return true;
                }
                LobbyPlayer lobbyPlayer = __instance as LobbyPlayer;
                if (lobbyPlayer != null && LocallyProtected(lobbyPlayer))
                {
                    Debug.Log("[LateJoin] blocking RemovePlayer for protected late joiner " + lobbyPlayer.networkNumber);
                    return false;
                }
                return true;
            }
        }

        // Purge guard B: direct ClientScene.RemovePlayer calls (SetupStart client
        // branch removes local players without a GameNetID).
        [HarmonyPatch(typeof(ClientScene), nameof(ClientScene.RemovePlayer))]
        static class ClientSceneRemovePlayerGuard
        {
            static bool Prefix(short playerControllerId, ref bool __result)
            {
                if (!LateJoinState.Enabled || !LateJoinState.ClientJoiningLate || LateJoinState.ClientIntegrated)
                {
                    return true;
                }
                var localPlayers = ClientScene.localPlayers;
                if (localPlayers == null || playerControllerId < 0 || playerControllerId >= localPlayers.Count)
                {
                    return true;
                }
                PlayerController playerController = localPlayers[playerControllerId];
                if (playerController == null || playerController.gameObject == null)
                {
                    return true;
                }
                LobbyPlayer lobbyPlayer = playerController.gameObject.GetComponent<LobbyPlayer>();
                if (lobbyPlayer != null && LocallyProtected(lobbyPlayer))
                {
                    Debug.Log("[LateJoin] blocking ClientScene.RemovePlayer for protected late joiner " + lobbyPlayer.networkNumber);
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        // ------------------------------------------------------------------
        // M4: pick flow (1005/1006), host-side spawn at ToPlaceMode, and the
        // idempotent 1004 activation relay applied on every peer.
        // ------------------------------------------------------------------

        // Register the activation handlers alongside the welcome ones (multiple
        // OnStartClient postfixes are fine).
        [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.OnStartClient))]
        static class RegisterActivationHandlersPatch
        {
            static void Postfix(LobbyManager __instance)
            {
                if (NetworkServer.active)
                {
                    NetworkServer.RegisterHandler(LateJoinMsgTypes.PickRequest, HandlePickRequestOnHost);
                }
                if (__instance.client != null)
                {
                    __instance.client.RegisterHandler(LateJoinMsgTypes.Activate, HandleActivateOnPeer);
                    __instance.client.RegisterHandler(LateJoinMsgTypes.PickResult, HandlePickResultOnPeer);
                }
            }
        }

        // Joiner side: called by LateJoinWelcome right after Hello when the
        // configured mode is "play". animal = -1 requests auto-pick.
        public static void SendPickRequest(LobbyPlayer lobbyPlayer)
        {
            MsgLateJoinPickRequest request = new MsgLateJoinPickRequest
            {
                networkNumber = lobbyPlayer.networkNumber,
                animal = -1
            };
            Debug.Log("[LateJoin] requesting character pick (auto) as " + request.networkNumber);
            LobbyManager.instance.client.Send(LateJoinMsgTypes.PickRequest, request);
        }

        // Host side: validate the pick, mark the LobbyPlayer picked (SyncVars
        // propagate), remember it for activation at the next ToPlaceMode.
        // CmdRequestPickCharacter is NOT reusable here: it NREs on
        // CurrentLevelSelectController outside the treehouse.
        static void HandlePickRequestOnHost(NetworkMessage netMsg)
        {
            MsgLateJoinPickRequest request = netMsg.ReadMessage<MsgLateJoinPickRequest>();
            LobbyPlayer joiner = LobbyManager.instance.GetLobbyPlayer(request.networkNumber);
            if (joiner == null)
            {
                Debug.LogError("[LateJoin] PickRequest from unknown networkNumber " + request.networkNumber);
                return;
            }

            HashSet<Character.Animals> taken = new HashSet<Character.Animals>();
            foreach (NetworkLobbyPlayer slot in LobbyManager.instance.lobbySlots)
            {
                LobbyPlayer lobbyPlayer = slot as LobbyPlayer;
                if (lobbyPlayer != null && lobbyPlayer.PickedAnimal != Character.Animals.NONE)
                {
                    taken.Add(lobbyPlayer.PickedAnimal);
                }
            }
            foreach (int pending in LateJoinState.PendingPicks.Values)
            {
                taken.Add((Character.Animals)pending);
            }

            Character.Animals picked = Character.Animals.NONE;
            if (request.animal > 0 && !taken.Contains((Character.Animals)request.animal))
            {
                picked = (Character.Animals)request.animal;
            }
            else
            {
                for (Character.Animals candidate = Character.Animals.CHICKEN; candidate <= Character.Animals.PLATYPUS; candidate++)
                {
                    if (!taken.Contains(candidate))
                    {
                        picked = candidate;
                        break;
                    }
                }
            }

            MsgLateJoinPickResult result = new MsgLateJoinPickResult
            {
                networkNumber = request.networkNumber,
                animal = (int)picked,
                ok = picked != Character.Animals.NONE
            };
            if (!result.ok)
            {
                Debug.LogWarning("[LateJoin] no free character for late joiner " + request.networkNumber);
                NetworkServer.SendToClientOfPlayer(joiner.gameObject, LateJoinMsgTypes.PickResult, result);
                return;
            }

            Debug.Log("[LateJoin] assigning " + picked + " to late joiner " + request.networkNumber);
            joiner.NetworkPickedAnimal = picked;
            joiner.NetworkplayerStatus = LobbyPlayer.Status.CHARACTER;
            LateJoinState.PendingPicks[request.networkNumber] = (int)picked;
            NetworkServer.SendToAll(LateJoinMsgTypes.PickResult, result);
        }

        static void HandlePickResultOnPeer(NetworkMessage netMsg)
        {
            MsgLateJoinPickResult result = netMsg.ReadMessage<MsgLateJoinPickResult>();
            Debug.Log("[LateJoin] pick result for " + result.networkNumber + ": animal=" +
                      (Character.Animals)result.animal + " ok=" + result.ok);
            // ok == false on the joiner: stay a protected, characterless lobby
            // player; the spectator handoff (or treehouse return) picks it up.
        }

        // Host side: at the start of every build phase, spawn GamePlayer +
        // Character + Cursor for each pending pick, then relay 1004 so every
        // peer (host included, via its local client) inserts the joiner.
        [HarmonyPatch(typeof(VersusControl), "ToPlaceMode")]
        static class ToPlaceModeActivationPatch
        {
            static void Prefix(VersusControl __instance)
            {
                if (!LateJoinState.Enabled || !__instance.hasAuthority || LateJoinState.PendingPicks.Count == 0)
                {
                    return;
                }
                foreach (KeyValuePair<int, int> pick in new List<KeyValuePair<int, int>>(LateJoinState.PendingPicks))
                {
                    ActivateOnHost(__instance, pick.Key, pick.Value);
                }
            }
        }

        static void ActivateOnHost(VersusControl gameControl, int networkNumber, int animal)
        {
            LobbyPlayer joiner = LobbyManager.instance.GetLobbyPlayer(networkNumber);
            if (joiner == null)
            {
                Debug.LogWarning("[LateJoin] pending pick " + networkNumber + " has no LobbyPlayer anymore - dropping");
                LateJoinState.PendingPicks.Remove(networkNumber);
                return;
            }
            NetworkConnection conn = joiner.connectionToClient;
            if (conn == null)
            {
                Debug.LogWarning("[LateJoin] pending pick " + networkNumber + " has no connection - dropping");
                LateJoinState.PendingPicks.Remove(networkNumber);
                return;
            }

            Debug.Log("[LateJoin] activating late joiner " + networkNumber + " as " + (Character.Animals)animal);

            // GamePlayer (mirrors OnLobbyServerSceneLoadedForPlayer +
            // NetworkLobbyManager.SceneLoadedForPlayer).
            GameObject gamePlayerObject = Object.Instantiate<GameObject>(LobbyManager.instance.gamePlayerPrefab);
            GamePlayer gamePlayer = gamePlayerObject.GetComponent<GamePlayer>();
            gamePlayer.NetworknetworkNumber = joiner.networkNumber;
            gamePlayer.NetworklocalNumber = joiner.localNumber;
            gamePlayer.NetworkPickedAnimal = joiner.PickedAnimal;
            gamePlayer.characterOutfitsList.Clear();
            foreach (int outfit in joiner.characterOutfitsList)
            {
                gamePlayer.characterOutfitsList.Add(outfit);
            }
            LobbyManager.instance.PlayerTracker.AddGamePlayer(gamePlayer);
            short playerControllerId = joiner.GetComponent<NetworkIdentity>().playerControllerId;
            NetworkServer.ReplacePlayerForConnection(conn, gamePlayerObject, playerControllerId);

            // Character + Cursor (exact copy of GameControl.SetupStart's host
            // block). global::Cursor disambiguates from UnityEngine.Cursor.
            Character character = Object.Instantiate<Character>(gameControl.CharacterPrefab);
            character.gameObject.name = gamePlayer.PickedAnimal.ToString();
            character.NetworkCharacterSprite = gamePlayer.PickedAnimal;
            character.SetOutfitsFromArray(gamePlayer.characterOutfitsList);
            character.NetworknetworkNumber = gamePlayer.networkNumber;
            character.NetworklocalNumber = gamePlayer.localNumber;
            character.Disable(true);
            character.NetworkFindPlayerOnSpawn = true;
            character.Networkpicked = true;
            NetworkServer.SpawnWithClientAuthority(character.gameObject, gamePlayer.gameObject);

            global::Cursor cursor = Object.Instantiate<global::Cursor>(gameControl.CursorPrefab);
            cursor.gameObject.name = gamePlayer.PickedAnimal.ToString() + " cursor";
            cursor.GetComponent<PiecePlacementCursor>().SetSprites(gamePlayer.PickedAnimal);
            cursor.NetworknetworkNumber = gamePlayer.networkNumber;
            cursor.NetworklocalNumber = gamePlayer.localNumber;
            cursor.SetBounds(gameControl.LevelLayout.GetCursorBounds());
            cursor.SetCursorColliderBounds(gameControl.LevelLayout.CursorBounds);
            cursor.Disable(false, false);
            cursor.NetworkFindPlayerOnSpawn = true;
            NetworkServer.SpawnWithClientAuthority(cursor.gameObject, gamePlayer.gameObject);

            gamePlayer.CallCmdAssignCharacter(character.gameObject, gamePlayer.networkNumber, gamePlayer.localNumber);
            gamePlayer.CallCmdAssignCursor(cursor.gameObject, gamePlayer.networkNumber, gamePlayer.localNumber);

            // Do NOT enqueue here - the 1004 relay does that on every peer
            // (including this host via its local client connection).
            LateJoinState.PendingPicks.Remove(networkNumber);
            MsgLateJoinActivate activate = new MsgLateJoinActivate
            {
                networkNumber = gamePlayer.networkNumber,
                animal = (int)gamePlayer.PickedAnimal,
                outfits = System.Linq.Enumerable.ToArray(gamePlayer.characterOutfitsList)
            };
            NetworkServer.SendToAll(LateJoinMsgTypes.Activate, activate);
        }

        // Every peer: wait for the spawned objects to arrive, then splice the
        // joiner into the running game (idempotent).
        static void HandleActivateOnPeer(NetworkMessage netMsg)
        {
            MsgLateJoinActivate activate = netMsg.ReadMessage<MsgLateJoinActivate>();
            if (LobbyManager.instance != null)
            {
                LobbyManager.instance.StartCoroutine(InsertJoinerWhenSpawned(activate));
            }
        }

        static IEnumerator InsertJoinerWhenSpawned(MsgLateJoinActivate activate)
        {
            float waited = 0f;
            GamePlayer gamePlayer = null;
            VersusControl gameControl = null;
            while (waited < 30f)
            {
                if (gamePlayer == null)
                {
                    foreach (GamePlayer candidate in Object.FindObjectsOfType<GamePlayer>())
                    {
                        if (candidate != null && candidate.networkNumber == activate.networkNumber)
                        {
                            gamePlayer = candidate;
                            break;
                        }
                    }
                }
                if (gameControl == null)
                {
                    gameControl = Object.FindObjectOfType<GameControl>() as VersusControl;
                }
                if (gamePlayer != null && gameControl != null &&
                    gamePlayer.CharacterInstance != null && gamePlayer.CursorInstance != null)
                {
                    break;
                }
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (gamePlayer == null || gameControl == null ||
                gamePlayer.CharacterInstance == null || gamePlayer.CursorInstance == null)
            {
                Debug.LogError("[LateJoin] activation of " + activate.networkNumber +
                               " timed out waiting for spawned objects (gamePlayer=" + (gamePlayer != null) +
                               " gameControl=" + (gameControl != null) + ")");
                yield break;
            }
            InsertJoiner(gameControl, gamePlayer, activate);
        }

        static void InsertJoiner(VersusControl gameControl, GamePlayer gamePlayer, MsgLateJoinActivate activate)
        {
            GameSettings settings = GameSettings.GetInstance();

            // Queue tail + per-peer TurnOrder (idempotent).
            if (!gameControl.PlayerQueue.Contains(gamePlayer))
            {
                gameControl.PlayerQueue.Enqueue(gamePlayer);
                gamePlayer.TurnOrder = gameControl.PlayerQueue.Count - 1;
            }
            gamePlayer.CursorInstance.UseCamera = gameControl.MainCamera.GetComponent<Camera>();

            // Scoreboard: grow, then re-run SetPlayerCharacter for every queue
            // index (mirrors SetupStart's dequeue/enqueue loop).
            if (gameControl.graphScoreBoardInstance != null)
            {
                gameControl.graphScoreBoardInstance.SetPlayerCount(gameControl.PlayerQueue.Count);
            }
            if (settings.GameMode == GameState.GameMode.PARTY && gameControl.partyBoxInstance != null)
            {
                gameControl.partyBoxInstance.SetPlayerCount(gameControl.PlayerQueue.Count);
            }
            for (int i = 0; i != gameControl.PlayerQueue.Count; i++)
            {
                GamePlayer queued = gameControl.PlayerQueue.Dequeue();
                LobbyPlayer lobbyPlayer = LobbyManager.instance.GetLobbyPlayer(queued.networkNumber);
                if (lobbyPlayer != null && gameControl.graphScoreBoardInstance != null && queued.CharacterInstance != null)
                {
                    gameControl.graphScoreBoardInstance.SetPlayerCharacter(i, queued.CharacterInstance.CharacterSprite,
                        queued.IsWearingSkin, lobbyPlayer, queued.Handicap);
                }
                gameControl.PlayerQueue.Enqueue(queued);
                if (gameControl.hasAuthority && settings.GameMode == GameState.GameMode.PARTY &&
                    queued.networkNumber == activate.networkNumber && gameControl.partyBoxInstance != null)
                {
                    PartyPickCursor partyPickCursor = gameControl.partyBoxInstance.AddPlayer(queued.networkNumber, queued.PickedAnimal);
                    queued.CallCmdAssignCursor(partyPickCursor.gameObject, queued.networkNumber, queued.localNumber);
                }
            }

            // CREATIVE: give the joiner placements for the round that is starting.
            if (settings.GameMode == GameState.GameMode.CREATIVE)
            {
                gameControl.RemainingPlacements[gamePlayer.networkNumber - 1] = settings.CreativePiecesPerRound;
            }

            // ScoreKeeper entry so tallies/scoreboard can resolve the joiner.
            var totals = ScoreKeeper.Instance.playerTotal;
            if (!totals.ContainsKey(gamePlayer))
            {
                totals[gamePlayer] = default(ScoreKeeper.scoreInfo);
            }

            if (gamePlayer.IsLocalPlayer)
            {
                // Joiner machine only: local control + inventory book wiring
                // (mirrors SetupStart lines for local players).
                if (gameControl.invBookInstance != null && gamePlayer.LocalPlayer != null)
                {
                    ((PiecePlacementCursor)gamePlayer.CursorInstance).InventoryBookMenu = gameControl.invBookInstance;
                    gameControl.invBookInstance.AddPlayer(gamePlayer.localNumber, gamePlayer.networkNumber,
                        gamePlayer.LocalPlayer.UseController, gamePlayer.CharacterInstance.CharacterSprite).Disable(true, false);
                }
                gamePlayer.Control.AddReceiver(gameControl);
                gamePlayer.CursorInstance.SetLocalController(gamePlayer.Control);
                gamePlayer.CharacterInstance.SetLocalController(gamePlayer.Control);
            }
            else
            {
                LobbyManager.instance.AllLocal = false;
            }

            // Joiner is a full player now - drop all late-join bookkeeping.
            LateJoinState.ProtectedNumbers.Remove(activate.networkNumber);
            LateJoinState.JoinerModes.Remove(activate.networkNumber);
            LateJoinState.PendingPicks.Remove(activate.networkNumber);
            if (gamePlayer.IsLocalPlayer && LateJoinState.ClientJoiningLate)
            {
                LateJoinState.ClientIntegrated = true;
            }
            Debug.Log("[LateJoin] late joiner " + activate.networkNumber + " inserted (queue=" +
                      gameControl.PlayerQueue.Count + " turnOrder=" + gamePlayer.TurnOrder + ")");
        }

        // ------------------------------------------------------------------
        // M4 step 4: spectator handoff once a "spectate"-mode joiner reaches the
        // treehouse. Null bridge delegate -> normal unpicked lobby player.
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(LevelSelectController), "SetupLobbyAfterWait")]
        static class SpectatorHandoffPatch
        {
            static void Postfix(LevelSelectController __instance)
            {
                // A player activated in the running level returns as its preserved
                // LobbyPlayer, so it does not emit LobbyPlayerCreatedEvent again.
                // Recreate the lobby-only objects that event normally supplies.
                RepairReturnedLateJoiner(__instance);

                if (!LateJoinState.Enabled || !LateJoinState.ClientJoiningLate || LateJoinState.ClientIntegrated)
                {
                    return;
                }
                if (MorePlayersMod.lateJoinMode == null || MorePlayersMod.lateJoinMode.Value != "spectate")
                {
                    return;
                }
                LobbyPlayer local = FindLocalLobbyPlayer();
                if (local == null)
                {
                    return;
                }
                if (LateJoinSpectatorBridge.OnLateJoinerWantsSpectate == null)
                {
                    Debug.Log("[LateJoin] spectator system unavailable - " + local.networkNumber + " becomes a normal lobby player");
                    LateJoinState.ProtectedNumbers.Remove(local.networkNumber);
                    LateJoinState.ClientIntegrated = true;
                    return;
                }
                Debug.Log("[LateJoin] handing " + local.networkNumber + " to the spectator system");
                LateJoinSpectatorBridge.OnLateJoinerWantsSpectate(local.networkNumber);
                LobbyManager.instance.StartCoroutine(ReleaseProtectionWhenSpectating(local.networkNumber));
            }
        }

        static readonly HashSet<int> LobbyRepairInProgress = new HashSet<int>();

        static void RepairReturnedLateJoiner(LevelSelectController levelSelect)
        {
            if (!LateJoinState.Enabled || levelSelect == null || LobbyManager.instance == null)
            {
                return;
            }

            // Cursor creation is server-authoritative. setupLobby already ran, so
            // a selected LobbyPlayer with neither lobby object is the preserved
            // match player, not a normal pick still in progress.
            if (NetworkServer.active && levelSelect.hasAuthority)
            {
                foreach (NetworkLobbyPlayer slot in LobbyManager.instance.lobbySlots)
                {
                    LobbyPlayer player = slot as LobbyPlayer;
                    if (NeedsLobbyRepair(player) && player.CursorInstance == null)
                    {
                        Debug.Log("[LateJoin] recreating treehouse cursor for returned player " + player.networkNumber);
                        levelSelect.CmdCreateCursorForPlayer(player.gameObject, true);
                    }
                }
            }

            LobbyPlayer local = FindLocalLobbyPlayer();
            if (NeedsLobbyRepair(local) && LobbyRepairInProgress.Add(local.networkNumber))
            {
                levelSelect.StartCoroutine(RepairLocalLobbyPlayer(levelSelect, local));
            }
        }

        static bool NeedsLobbyRepair(LobbyPlayer player)
        {
            return player != null &&
                   player.PickedAnimal != Character.Animals.NONE &&
                   player.CharacterInstance == null;
        }

        static IEnumerator RepairLocalLobbyPlayer(LevelSelectController levelSelect, LobbyPlayer player)
        {
            int networkNumber = player.networkNumber;
            float waited = 0f;
            while (waited < 15f && player != null && player.CursorInstance == null)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            try
            {
                if (player == null || player.CursorInstance == null || player.LocalPlayer == null ||
                    player.LocalPlayer.UseController == null)
                {
                    Debug.LogError("[LateJoin] treehouse repair timed out for returned player " + networkNumber);
                    yield break;
                }

                Controller controller = player.LocalPlayer.UseController;
                if (!controller.ControlsPlayer(player.LocalPlayer.Number))
                {
                    controller.AddPlayer(player.LocalPlayer.Number);
                }
                controller.AssociateCharacter(player.PickedAnimal, player.LocalPlayer.Number);
                levelSelect.setupController(player);

                if (player.CharacterInstance == null)
                {
                    Debug.LogError("[LateJoin] treehouse character repair failed for returned player " + networkNumber);
                    yield break;
                }

                LateJoinState.ProtectedNumbers.Remove(networkNumber);
                LateJoinState.JoinerModes.Remove(networkNumber);
                if (player.IsLocalPlayer && LateJoinState.ClientJoiningLate)
                {
                    LateJoinState.ClientIntegrated = true;
                }
                Debug.Log("[LateJoin] restored returned player " + networkNumber + " as " + player.PickedAnimal);
            }
            finally
            {
                LobbyRepairInProgress.Remove(networkNumber);
            }
        }

        static IEnumerator ReleaseProtectionWhenSpectating(int networkNumber)
        {
            while (LateJoinSpectatorBridge.IsSpectating == null || !LateJoinSpectatorBridge.IsSpectating(networkNumber))
            {
                if (!LateJoinState.ClientJoiningLate)
                {
                    yield break; // disconnected/reset in the meantime
                }
                yield return null;
            }
            Debug.Log("[LateJoin] " + networkNumber + " is spectating - protection handed over");
            LateJoinState.ProtectedNumbers.Remove(networkNumber);
            LateJoinState.ClientIntegrated = true;
        }

        static LobbyPlayer FindLocalLobbyPlayer()
        {
            if (LobbyManager.instance == null || LobbyManager.instance.lobbySlots == null)
            {
                return null;
            }
            foreach (NetworkLobbyPlayer slot in LobbyManager.instance.lobbySlots)
            {
                LobbyPlayer lobbyPlayer = slot as LobbyPlayer;
                if (lobbyPlayer != null && lobbyPlayer.isLocalPlayer)
                {
                    return lobbyPlayer;
                }
            }
            return null;
        }
    }
}
