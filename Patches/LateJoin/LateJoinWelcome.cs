using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace MorePlayers.LateJoin
{
    // The mid-level "welcome packet": a late joiner announces itself (Hello) once
    // its LobbyPlayer exists, and the host unicasts everything the vanilla
    // treehouse-only ClientLoadedTreehouse handshake would have delivered, plus
    // mod-specific state (current phase/round, match scores, placed pieces).
    //
    // Vanilla auto-spawn already delivers all networked objects and their
    // SyncVars; this fills the gaps that travel via unbuffered messages.
    public static class LateJoinWelcome
    {
        static bool helloSent;

        // Register handlers alongside the vanilla ones.
        [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.OnStartClient))]
        static class RegisterHandlersPatch
        {
            static void Postfix(LobbyManager __instance)
            {
                helloSent = false;
                if (NetworkServer.active)
                {
                    NetworkServer.RegisterHandler(LateJoinMsgTypes.Hello, HandleHelloOnHost);
                }
                if (__instance.client != null)
                {
                    __instance.client.RegisterHandler(LateJoinMsgTypes.GameState, HandleGameStateOnClient);
                    __instance.client.RegisterHandler(LateJoinMsgTypes.Scores, HandleScoresOnClient);
                }
            }
        }

        // Client side: send Hello once our local LobbyPlayer is fully initialized
        // while joining a running match outside the treehouse.
        [HarmonyPatch(typeof(LobbyPlayer), "Start")]
        static class LobbyPlayerStartPatch
        {
            static void Postfix(LobbyPlayer __instance)
            {
                if (LateJoinState.Enabled && LateJoinState.ClientJoiningLate && !NetworkServer.active)
                {
                    __instance.StartCoroutine(SendHelloWhenReady(__instance));
                }
            }
        }

        static IEnumerator SendHelloWhenReady(LobbyPlayer lobbyPlayer)
        {
            while (lobbyPlayer != null && (!lobbyPlayer.isLocalPlayer || lobbyPlayer.networkNumber <= 0 || lobbyPlayer.netId.IsEmpty()))
            {
                if (!lobbyPlayer.isLocalPlayer && lobbyPlayer.networkNumber > 0)
                {
                    yield break; // remote player's object, not ours
                }
                yield return null;
            }
            if (lobbyPlayer == null || helloSent || !LateJoinState.ClientJoiningLate)
            {
                yield break;
            }
            // Direct-IP learns that this is a late join from the server's scene
            // message, after its LobbyPlayer may already have started. Wait for
            // the directed level load instead of treating the current treehouse
            // frame as a normal lobby join.
            while (lobbyPlayer != null && LateJoinState.ClientJoiningLate
                && SceneManager.GetActiveScene().name == "TreeHouseLobby")
            {
                yield return null;
            }
            if (lobbyPlayer == null || helloSent || !LateJoinState.ClientJoiningLate)
            {
                yield break;
            }
            helloSent = true;
            MsgLateJoinHello hello = new MsgLateJoinHello
            {
                networkNumber = lobbyPlayer.networkNumber,
                requestedMode = (byte)(MorePlayersMod.lateJoinMode != null && MorePlayersMod.lateJoinMode.Value == "spectate" ? 1 : 0)
            };
            Debug.Log("[LateJoin] sending Hello as networkNumber " + hello.networkNumber + " (mode " + hello.requestedMode + ")");
            LobbyManager.instance.client.Send(LateJoinMsgTypes.Hello, hello);
            if (hello.requestedMode == 0)
            {
                // "play" mode: immediately ask for a character so the host can
                // activate us at the next build phase (auto-pick).
                LateJoinActivation.SendPickRequest(lobbyPlayer);
            }
        }

        public static void SendHelloForExistingLocalPlayer()
        {
            if (helloSent || LobbyManager.instance == null)
                return;

            foreach (NetworkLobbyPlayer slot in LobbyManager.instance.lobbySlots)
            {
                LobbyPlayer lobbyPlayer = slot as LobbyPlayer;
                if (lobbyPlayer != null && lobbyPlayer.isLocalPlayer)
                {
                    lobbyPlayer.StartCoroutine(SendHelloWhenReady(lobbyPlayer));
                    return;
                }
            }
        }

        // Host side: answer Hello with the full welcome bundle.
        static void HandleHelloOnHost(NetworkMessage netMsg)
        {
            MsgLateJoinHello hello = netMsg.ReadMessage<MsgLateJoinHello>();
            LobbyPlayer joiner = LobbyManager.instance.GetLobbyPlayer(hello.networkNumber);
            if (joiner == null)
            {
                Debug.LogError("[LateJoin] Hello from unknown networkNumber " + hello.networkNumber);
                return;
            }
            Debug.Log("[LateJoin] Hello from " + hello.networkNumber + " (mode " + hello.requestedMode + ") - sending welcome packet");
            LateJoinState.JoinerModes[hello.networkNumber] = hello.requestedMode;
            LateJoinState.ProtectedNumbers.Add(hello.networkNumber);

            GameObject joinerObject = joiner.gameObject;
            GameSettings settings = GameSettings.GetInstance();

            // 1. Game mode (consumed by LobbyManager.handleEvent on any scene).
            MsgSwitchToMode switchToMode = new MsgSwitchToMode { toMode = settings.GameMode };
            NetworkServer.SendToClientOfPlayer(joinerObject, NetMsgTypes.SwitchToMode, switchToMode);

            // 2. Rules (replicates LevelSelectController.SendAllRules, which is
            //    unavailable outside the treehouse).
            bool destroyPreset = false;
            GameRulePreset preset;
            if (settings.HasDirtyRuleset)
            {
                preset = ScriptableObject.CreateInstance<GameRulePreset>();
                preset.Name = null;
                preset.Description = null;
                preset.LoadRulesFromSettings();
                destroyPreset = true;
            }
            else
            {
                preset = settings.GetCurrentRuleset();
            }
            MsgApplyRuleset applyRuleset = preset.GenerateApplyRulesetMessage(true, true, true, true);
            applyRuleset.temporary = true;
            NetworkServer.SendToClientOfPlayer(joinerObject, NetMsgTypes.ApplyRuleset, applyRuleset);
            if (destroyPreset)
            {
                Object.Destroy(preset);
            }

            // 3. AFK kick time.
            MsgGameRuleSet afkRule = new MsgGameRuleSet
            {
                NewRule = TabletRule.OnlineSettingsAFKKickTime,
                Value = settings.AFKAutoKickTime
            };
            NetworkServer.SendToClientOfPlayer(joinerObject, NetMsgTypes.GameRuleSet, afkRule);

            // 4. Character outfits (same subset the treehouse handshake sends).
            foreach (Character character in Character.AllCharacters)
            {
                if (character == null)
                {
                    continue;
                }
                int[] outfits = character.GetOutfitsAsArray();
                if (outfits[0] != -1 || outfits[1] != -1 || outfits[2] != -1 || outfits[3] != -1)
                {
                    MsgCommunicateCharacterOutfits outfitsMsg = new MsgCommunicateCharacterOutfits
                    {
                        Animal = character.CharacterSprite,
                        OutfitArray = outfits
                    };
                    NetworkServer.SendToClientOfPlayer(joinerObject, NetMsgTypes.CommunicateCharacterOutfits, outfitsMsg);
                }
            }

            // 5. Phase/round snapshot (mod message).
            GameControl gameControl = Object.FindObjectOfType<GameControl>();
            MsgLateJoinGameState state = new MsgLateJoinGameState
            {
                sceneName = SceneManager.GetActiveScene().name,
                phase = gameControl != null ? (int)gameControl.Phase : 0,
                roundNumber = gameControl != null ? gameControl.roundNumber : 0,
                gameMode = (int)settings.GameMode,
                partyBox = settings.GameMode == GameState.GameMode.PARTY,
                placementTimer = 0f
            };
            NetworkServer.SendToClientOfPlayer(joinerObject, LateJoinMsgTypes.GameState, state);

            // 6. Scores.
            SendScoresTo(joinerObject);

            // 7. Placed-piece replay: one vanilla MsgPiecePlaced per placed block,
            //    applied on the joiner by its copy of the host's placement cursor.
            if (gameControl != null)
            {
                int senderNumber = FindHostNetworkNumber();
                int replayed = 0;
                foreach (Placeable placeable in gameControl.placedBlocks)
                {
                    if (placeable == null || !placeable.Placed)
                    {
                        continue;
                    }
                    MsgPiecePlaced pieceMsg = new MsgPiecePlaced
                    {
                        PlayerNumber = senderNumber,
                        PiecePosition = placeable.transform.position,
                        PieceScale = placeable.transform.localScale,
                        PieceRotation = placeable.transform.rotation,
                        PieceID = placeable.ID,
                        PieceWasMoved = false,
                        ResetPosition = false
                    };
                    NetworkServer.SendToClientOfPlayer(joinerObject, NetMsgTypes.PiecePlaced, pieceMsg);
                    replayed++;
                }
                Debug.Log("[LateJoin] replayed " + replayed + " placed pieces to " + hello.networkNumber);
            }
        }

        static int FindHostNetworkNumber()
        {
            foreach (NetworkLobbyPlayer slot in LobbyManager.instance.lobbySlots)
            {
                LobbyPlayer lobbyPlayer = slot as LobbyPlayer;
                if (lobbyPlayer != null && lobbyPlayer.isLocalPlayer)
                {
                    return lobbyPlayer.networkNumber;
                }
            }
            return 1;
        }

        // Host side: build and unicast the score snapshot.
        public static void SendScoresTo(GameObject joinerObject)
        {
            var scores = ScoreKeeper.Instance.playerTotal;
            var msg = new MsgLateJoinScores
            {
                networkNumbers = new int[scores.Count],
                totalScores = new int[scores.Count],
                winStreaks = new int[scores.Count],
                loseStreaks = new int[scores.Count],
                disconnected = new bool[scores.Count]
            };
            int i = 0;
            foreach (KeyValuePair<GamePlayer, ScoreKeeper.scoreInfo> entry in scores)
            {
                if (entry.Key == null)
                {
                    continue;
                }
                msg.networkNumbers[i] = entry.Key.networkNumber;
                msg.totalScores[i] = entry.Value.totalScore;
                msg.winStreaks[i] = entry.Value.winStreak;
                msg.loseStreaks[i] = entry.Value.loseStreak;
                msg.disconnected[i] = entry.Value.disconnected;
                i++;
            }
            if (i != msg.networkNumbers.Length)
            {
                System.Array.Resize(ref msg.networkNumbers, i);
                System.Array.Resize(ref msg.totalScores, i);
                System.Array.Resize(ref msg.winStreaks, i);
                System.Array.Resize(ref msg.loseStreaks, i);
                System.Array.Resize(ref msg.disconnected, i);
            }
            NetworkServer.SendToClientOfPlayer(joinerObject, LateJoinMsgTypes.Scores, msg);
        }

        // Host side: refresh the joiner's scores after every tally while it is
        // still waiting to be activated (idempotent overwrite).
        [HarmonyPatch(typeof(ScoreKeeper), nameof(ScoreKeeper.TallyPointBlockAllPlayers))]
        static class TallyRefreshPatch
        {
            static void Postfix()
            {
                if (!LateJoinState.Enabled || !NetworkServer.active || LateJoinState.JoinerModes.Count == 0)
                {
                    return;
                }
                foreach (int networkNumber in new List<int>(LateJoinState.JoinerModes.Keys))
                {
                    LobbyPlayer joiner = LobbyManager.instance != null ? LobbyManager.instance.GetLobbyPlayer(networkNumber) : null;
                    if (joiner != null)
                    {
                        SendScoresTo(joiner.gameObject);
                    }
                }
            }
        }

        // Joiner side: cache the phase/round snapshot.
        static void HandleGameStateOnClient(NetworkMessage netMsg)
        {
            MsgLateJoinGameState state = netMsg.ReadMessage<MsgLateJoinGameState>();
            LateJoinState.LastGameState = state;
            Debug.Log("[LateJoin] game state: scene=" + state.sceneName + " phase=" + state.phase +
                      " round=" + state.roundNumber + " mode=" + state.gameMode);
            if (LobbyManager.instance != null)
            {
                LobbyManager.instance.StartCoroutine(ApplyGameStateWhenReady(state));
            }
        }

        // The joiner misses the RpcStartPhase that the host sent before it
        // connected. GameControl's initial spawn does not serialize Phase, so it
        // otherwise remains NONE and WaitForPhaseAndFadeOut leaves the loading
        // splash up forever. Replay the missed RPC locally once GameControl has
        // spawned; its normal update path performs the phase setup and fade-out.
        static IEnumerator ApplyGameStateWhenReady(MsgLateJoinGameState state)
        {
            float waited = 0f;
            GameControl gameControl = null;
            while (waited < 30f)
            {
                gameControl = Object.FindObjectOfType<GameControl>();
                if (gameControl != null)
                {
                    break;
                }
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (gameControl == null)
            {
                Debug.LogError("[LateJoin] timed out applying game phase " + state.phase);
                yield break;
            }

            GameControl.GamePhase phase = (GameControl.GamePhase)state.phase;
            if (gameControl.Phase != phase)
            {
                Debug.Log("[LateJoin] replaying missed phase transition " + phase);
                gameControl.RpcStartPhase(phase);
            }
        }

        // Joiner side: apply the score snapshot once the GamePlayer objects have
        // spawned locally.
        static void HandleScoresOnClient(NetworkMessage netMsg)
        {
            MsgLateJoinScores scores = netMsg.ReadMessage<MsgLateJoinScores>();
            if (LobbyManager.instance != null)
            {
                LobbyManager.instance.StartCoroutine(ApplyScoresWhenResolvable(scores));
            }
        }

        static IEnumerator ApplyScoresWhenResolvable(MsgLateJoinScores scores)
        {
            float waited = 0f;
            while (waited < 30f)
            {
                int resolved = ApplyScores(scores, false);
                if (resolved == scores.networkNumbers.Length)
                {
                    break;
                }
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            ApplyScores(scores, true);
        }

        static int ApplyScores(MsgLateJoinScores scores, bool final)
        {
            int resolved = 0;
            GamePlayer[] gamePlayers = Object.FindObjectsOfType<GamePlayer>();
            var totals = ScoreKeeper.Instance.playerTotal;
            for (int i = 0; i < scores.networkNumbers.Length; i++)
            {
                GamePlayer match = null;
                foreach (GamePlayer gamePlayer in gamePlayers)
                {
                    if (gamePlayer != null && gamePlayer.networkNumber == scores.networkNumbers[i])
                    {
                        match = gamePlayer;
                        break;
                    }
                }
                if (match == null)
                {
                    if (final)
                    {
                        Debug.LogWarning("[LateJoin] no GamePlayer for score entry networkNumber " + scores.networkNumbers[i]);
                    }
                    continue;
                }
                resolved++;
                if (final)
                {
                    totals[match] = new ScoreKeeper.scoreInfo
                    {
                        totalScore = scores.totalScores[i],
                        winStreak = scores.winStreaks[i],
                        loseStreak = scores.loseStreaks[i],
                        disconnected = scores.disconnected[i]
                    };
                }
            }
            if (final)
            {
                Debug.Log("[LateJoin] applied score snapshot for " + resolved + "/" + scores.networkNumbers.Length + " players");
            }
            return resolved;
        }

        // Everywhere: once a protected joiner picks a character it is a normal
        // player again - drop the protection.
        [HarmonyPatch(typeof(LobbyPlayer), nameof(LobbyPlayer.DoCharacterPickedEvent))]
        static class CharacterPickedClearsProtectionPatch
        {
            static void Postfix(LobbyPlayer __instance)
            {
                if (!LateJoinState.Enabled || __instance.PickedAnimal == Character.Animals.NONE)
                {
                    return;
                }
                if (LateJoinState.ProtectedNumbers.Remove(__instance.networkNumber))
                {
                    Debug.Log("[LateJoin] " + __instance.networkNumber + " picked a character - protection dropped");
                }
                LateJoinState.JoinerModes.Remove(__instance.networkNumber);
                if (__instance.IsLocalPlayer && LateJoinState.ClientJoiningLate)
                {
                    LateJoinState.ClientIntegrated = true;
                }
            }
        }
    }
}
