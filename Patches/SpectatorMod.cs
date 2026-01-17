using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;
using UnityEngine.UI;
using System;
using System.Collections;
using GameEvent;

namespace MorePlayers
{
    [HarmonyPatch]
    static class SpectatorModPatches
    {
        // Static dictionary to track spectator players
        private static readonly Dictionary<int, bool> spectatorPlayers = new Dictionary<int, bool>();
        
        // Spectator status event class (simplified for spectator-specific needs)
        public class SpectatorStatusEvent : GameEvent.GameEvent
        {
            public readonly int PlayerNumber;
            public readonly bool IsSpectator;
            
            public SpectatorStatusEvent(int playerNumber, bool isSpectator)
            {
                PlayerNumber = playerNumber;
                IsSpectator = isSpectator;
            }
        }
        
        // Static dictionary to track recent spectators (to prevent cursor creation)
        private static readonly Dictionary<int, float> recentSpectators = new Dictionary<int, float>();
        
        // Static flag to track if couch text has been updated
        private static bool couchTextUpdated = false;
        
        // Reset couch customization when lobby is destroyed
        [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.OnDestroy))]
        static class LobbyManagerOnDestroyPatch
        {
            static void Prefix()
            {
                if (MorePlayersMod.spectatorMode.Value)
                {
                    Debug.Log("[SpectatorMod] Lobby destroyed - resetting couch customization");
                    couchTextUpdated = false;
                }
            }
        }
        
        // Static flag to prevent rapid re-entry (within 1 second)
        private static readonly Dictionary<int, float> lastSpectatorExit = new Dictionary<int, float>();

        // Helper method to check if player is spectator
        private static bool IsSpectator(int networkPlayerNumber)
        {
            return spectatorPlayers.ContainsKey(networkPlayerNumber) && spectatorPlayers[networkPlayerNumber];
        }

        // Helper method to check if player was recently a spectator (within last 3 seconds)
        private static bool IsRecentSpectator(int networkPlayerNumber)
        {
            if (!recentSpectators.ContainsKey(networkPlayerNumber))
                return false;
            
            float timeSinceExit = Time.time - recentSpectators[networkPlayerNumber];
            return timeSinceExit < 3f; // 3 seconds
        }

        // Helper method to check if player recently exited spectator mode (within 1 second)
        private static bool RecentlyExitedSpectator(int networkPlayerNumber)
        {
            if (!lastSpectatorExit.ContainsKey(networkPlayerNumber))
                return false;
            
            float timeSinceExit = Time.time - lastSpectatorExit[networkPlayerNumber];
            return timeSinceExit < 1f; // 1 second
        }

        // Helper method to call spectator status command (added via Harmony)
        private static void CallCmdSetSpectatorStatus(LobbyPlayer lobbyPlayer, bool isSpectator)
        {
            try
            {
                // This will be patched into LobbyPlayer via Harmony
                // For now, we'll use a direct approach similar to existing commands
                if (NetworkServer.active)
                {
                    // Server-side: directly set the status and broadcast
                    SetSpectator(lobbyPlayer.networkNumber, isSpectator);
                }
                else if (NetworkClient.active)
                {
                    // Client-side: send request to server using existing network infrastructure
                    // We'll use the PlayerStatus system which is already network-synced
                    lobbyPlayer.PlayerStatus = isSpectator ? LobbyPlayer.Status.COUCH : LobbyPlayer.Status.CHARACTER;
                    Debug.Log($"[SpectatorMod] Set spectator status via PlayerStatus for player {lobbyPlayer.networkNumber}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpectatorMod] Exception in CallCmdSetSpectatorStatus: {ex.Message}");
            }
        }

        // Helper method to request spectator sitdown from server (using our custom network system)
        private static void RequestSpectatorSitdown(int playerNumber)
        {
            try
            {
                if (LobbyManager.instance != null && NetworkClient.active)
                {
                    Debug.Log($"[SpectatorMod] Client requesting spectator sitdown for player {playerNumber}");
                    
                    // Create and send spectator status message to server
                    SpectatorStatusMessage msg = new SpectatorStatusMessage
                    {
                        networkPlayerNumber = playerNumber,
                        isSpectator = true
                    };
                    
                    NetworkClient.allClients[0].Send(SPECTATOR_STATUS_MSG_TYPE, msg);
                    Debug.Log($"[SpectatorMod] Sent spectator sitdown request for player {playerNumber}");
                }
                else
                {
                    Debug.LogWarning($"[SpectatorMod] Cannot request spectator sitdown - no active network connection");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpectatorMod] Exception requesting spectator sitdown: {ex.Message}");
            }
        }

        // Helper method to request spectator unsit from server (using our custom network system)
        private static void RequestSpectatorUnsit(int playerNumber)
        {
            try
            {
                if (LobbyManager.instance != null && NetworkClient.active)
                {
                    Debug.Log($"[SpectatorMod] Client requesting spectator unsit for player {playerNumber}");
                    
                    // Create and send spectator status message to server
                    SpectatorStatusMessage msg = new SpectatorStatusMessage
                    {
                        networkPlayerNumber = playerNumber,
                        isSpectator = false
                    };
                    
                    NetworkClient.allClients[0].Send(SPECTATOR_STATUS_MSG_TYPE, msg);
                    Debug.Log($"[SpectatorMod] Sent spectator unsit request for player {playerNumber}");
                }
                else
                {
                    Debug.LogWarning($"[SpectatorMod] Cannot request spectator unsit - no active network connection");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpectatorMod] Exception requesting spectator unsit: {ex.Message}");
            }
        }

        // Helper method to set spectator status
        private static void SetSpectator(int networkPlayerNumber, bool isSpectator)
        {
            spectatorPlayers[networkPlayerNumber] = isSpectator;
            Debug.Log($"[SpectatorMod] Player {networkPlayerNumber} spectator status set to: {isSpectator}");
            
            // Network sync - send to all clients if we're server
            if (NetworkServer.active)
            {
                SendSpectatorStatusUpdate(networkPlayerNumber, isSpectator);
            }
            else if (NetworkClient.active)
            {
                // If we're a client, request server sync
                if (isSpectator)
                {
                    // Client becoming spectator - request sitdown from server
                    RequestSpectatorSitdown(networkPlayerNumber);
                }
                else
                {
                    // Client leaving spectator mode - request unsit from server
                    RequestSpectatorUnsit(networkPlayerNumber);
                }
            }
            
            // Track when spectator exits
            if (!isSpectator)
            {
                recentSpectators[networkPlayerNumber] = Time.time;
                lastSpectatorExit[networkPlayerNumber] = Time.time;
                Debug.Log($"[SpectatorMod] Player {networkPlayerNumber} marked as recent spectator at {Time.time}");
            }
        }

        // Show couch in online mode for spectator functionality
        [HarmonyPatch(typeof(HotSeat), nameof(HotSeat.Update))]
        static class HotSeatUpdatePatch
        {
            static bool Prefix(HotSeat __instance)
            {
                if (LobbyManager.instance != null && MorePlayersMod.spectatorMode.Value)
                {
                    // Always show couch, even in online mode
                    __instance.show();
                    
                    // Change couch text to "Spectator Couch" only once
                    if (!couchTextUpdated)
                    {
                        UpdateCouchText(__instance);
                        couchTextUpdated = true;
                    }
                    
                    return false; // Skip original method
                }
                return true; // Continue with original method if spectator mode is disabled
            }
            
            private static void UpdateCouchText(HotSeat hotSeat)
            {
                // Find Text components in HotSeat GameObject or its children
                Text[] textComponents = hotSeat.GetComponentsInChildren<Text>();
                foreach (Text text in textComponents)
                {
                    if (text.text != null && (text.text.ToLower().Contains("couch") || text.text.ToLower().Contains("hot")))
                    {
                        text.text = "Spectator Couch";
                        Debug.Log("[SpectatorMod] Changed couch text to 'Spectator Couch'");
                    }
                }
                
                // Change couch color to green
                ChangeCouchColor(hotSeat);
            }
            
            private static void ChangeCouchColor(HotSeat hotSeat)
            {
                // Find all Renderer components in HotSeat GameObject and its children
                Renderer[] renderers = hotSeat.GetComponentsInChildren<Renderer>();
                foreach (Renderer renderer in renderers)
                {
                    if (renderer.material != null)
                    {
                        // Change material color to green with some transparency
                        Color greenColor = new Color(0f, 1f, 0f, 0.8f); // Green with 80% opacity
                        renderer.material.color = greenColor;
                        Debug.Log($"[SpectatorMod] Changed {renderer.gameObject.name} color to green");
                    }
                }
                
                // Also change SpriteRenderer colors
                SpriteRenderer[] spriteRenderers = hotSeat.GetComponentsInChildren<SpriteRenderer>();
                foreach (SpriteRenderer spriteRenderer in spriteRenderers)
                {
                    if (spriteRenderer.color != null)
                    {
                        // Change sprite color to green
                        Color greenColor = new Color(0f, 1f, 0f, 1f); // Full green
                        spriteRenderer.color = greenColor;
                        Debug.Log($"[SpectatorMod] Changed sprite {spriteRenderer.gameObject.name} color to green");
                    }
                }
            }
        }

        // Hook the original couch sitdown logic to handle spectator mode
        [HarmonyPatch(typeof(LevelSelectController), "ReceiveEvent")]
        static class LevelSelectControllerReceiveEventPatch
        {
            static bool Prefix(LevelSelectController __instance, InputEvent e)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return true; // Continue normally if spectator mode is disabled

                int controlMask = e.Sender.GetControlMask();
                
                // Handle spectator exit logic (B button/Right-click/Jump button/ESC button)
                if ((!e.Sender.IsKeyboard || !Controller.InputFieldWasActiveRecently) && 
                    (e.Key == InputEvent.InputKey.Back || e.Key == InputEvent.InputKey.Jump || e.Key == InputEvent.InputKey.Esc) && e.Valueb && e.Changed)
                {
                    Debug.Log($"[SpectatorMod] Jump/Back/ESC button detected: {e.Key}, Value: {e.Valueb}, Changed: {e.Changed}");
                    
                    for (int num = 0; num != __instance.JoinedPlayers.Length; num++)
                    {
                        LobbyPlayer lobbyPlayer2 = __instance.JoinedPlayers[num];
                        if (controlMask > 0 && lobbyPlayer2 != null && e.Sender.ControlsPlayer(lobbyPlayer2.localNumber))
                        {
                            Debug.Log($"[SpectatorMod] Checking player {num}: Status={lobbyPlayer2.PlayerStatus}, IsSpectator={IsSpectator(lobbyPlayer2.networkNumber)}, LocalPlayer={lobbyPlayer2.LocalPlayer != null}");
                            
                            // Check if this is a spectator sitting on the couch
                            bool isSpectatorStatus = lobbyPlayer2.PlayerStatus == LobbyPlayer.Status.COUCH;
                            bool isSpectator = IsSpectator(lobbyPlayer2.networkNumber);
                            bool isSitting = lobbyPlayer2.LocalPlayer != null && __instance.HotSeatCouch.PlayerSitting(lobbyPlayer2.LocalPlayer);
                            
                            Debug.Log($"[SpectatorMod] Exit conditions: COUCH={isSpectatorStatus}, IsSpectator={isSpectator}, PlayerSitting={isSitting}");
                            
                            if (isSpectatorStatus && isSpectator && isSitting)
                            {
                                Debug.Log($"[SpectatorMod] Player {lobbyPlayer2.networkNumber} attempting to leave spectator mode via {e.Key}");
                                
                                // Get correct player for this spectator (networked players need networkNumber)
                                Player exitPlayer = lobbyPlayer2.LocalPlayer;
                                
                                if (exitPlayer == null)
                                {
                                    Debug.LogError($"[SpectatorMod] ERROR: Could not find player for spectator exit. localNumber={lobbyPlayer2.localNumber}, networkNumber={lobbyPlayer2.networkNumber}");
                                    return false;
                                }
                                
                                Debug.Log($"[SpectatorMod] Unsitting player {exitPlayer.Number} from couch");
                                
                                // Handle couch unsitting based on network context
                                if (NetworkServer.active || !NetworkClient.active)
                                {
                                    // Local game or server - we can unsit from the couch
                                    __instance.HotSeatCouch.UnsitPlayer(exitPlayer);
                                    Debug.Log($"[SpectatorMod] Local game: Unsitted player {exitPlayer.Number} from couch");
                                }
                                else
                                {
                                    // Network client - spectator status will be synced via network
                                    // Visual representation will be handled by each client receiving the sync
                                    Debug.Log($"[SpectatorMod] Network client: Spectator exit will be synced via network");
                                }
                                
                                __instance.PlayerJoinIndicators[num].PickLevelEnabled();
                                lobbyPlayer2.PlayerStatus = LobbyPlayer.Status.CHARACTER;
                                
                                // Clear spectator status
                                SetSpectator(lobbyPlayer2.networkNumber, false);
                                
                                // Prevent cursor spawning for this player
                                if (__instance.GameRuleBook != null && __instance.GameRuleBook.GetCursor(lobbyPlayer2.networkNumber) != null)
                                {
                                    Debug.Log($"[SpectatorMod] Found existing cursor for player {lobbyPlayer2.networkNumber}, removing it");
                                    PickCursor cursor = __instance.GameRuleBook.GetCursor(lobbyPlayer2.networkNumber);
                                    cursor.Freeze();
                                    cursor.Disable(true, false);
                                    __instance.GameRuleBook.RemovePlayer(lobbyPlayer2.networkNumber, e.Sender);
                                }
                                else
                                {
                                    Debug.Log($"[SpectatorMod] No existing cursor found for player {lobbyPlayer2.networkNumber}");
                                }
                                
                                // IMPORTANT: Return false to prevent original method from continuing
                                // This prevents Jump button from immediately re-sitting the player or triggering shared couch behavior
                                return false;
                            }
                        }
                    }
                }
                
                // Handle spectator entry logic (Accept button) - this replaces the original couch sitdown
                if ((!e.Sender.IsKeyboard || !Controller.InputFieldWasActiveRecently) && e.Key == InputEvent.InputKey.Accept && e.Valueb && e.Changed)
                {
                    for (int num = 0; num != __instance.JoinedPlayers.Length; num++)
                    {
                        LobbyPlayer lobbyPlayer2 = __instance.JoinedPlayers[num];
                        if (controlMask > 0 && lobbyPlayer2 != null && e.Sender.ControlsPlayer(lobbyPlayer2.localNumber))
                        {
                            // Check if this player should become a spectator
                            if (lobbyPlayer2.PlayerStatus == LobbyPlayer.Status.CHARACTER && __instance.HotSeatCouch.IsSeatAvailable())
                            {
                                // Try to get player character

                                Player player = PlayerManager.GetInstance().GetPlayer(lobbyPlayer2.localNumber);
                                Character playerCharacter = player.PlayerCharacter;

                                if (playerCharacter == null)
                                {
                                    Debug.LogError($"[SpectatorMod] ERROR: Could not find player for spectator entry. localNumber={lobbyPlayer2.localNumber}, networkNumber={lobbyPlayer2.networkNumber}");
                                    return false;
                                }
                                
                                // Check if character is at couch and not recently exited spectator
                                bool characterAtCouch = playerCharacter != null && __instance.HotSeatCouch.CharacterAtCouch(playerCharacter);
                                bool characterInMenu = playerCharacter != null && playerCharacter.InMenu;
                                int spectatorPlayerNumber = lobbyPlayer2.networkNumber;
                                bool notRecentlyExited = !RecentlyExitedSpectator(spectatorPlayerNumber);
                                
                                Debug.Log($"[SpectatorMod] Entry conditions: CharacterAtCouch={characterAtCouch}, NotInMenu={!characterInMenu}, NotRecentlyExited={notRecentlyExited}");
                                
                                if (characterAtCouch && !characterInMenu && notRecentlyExited && player != null)
                                {
                                    Debug.Log($"[SpectatorMod] Player {spectatorPlayerNumber} becoming spectator");
                                    
                                    // Set spectator status and handle network sync
                                    SetSpectator(lobbyPlayer2.networkNumber, true);
                                    
                                    // Update player status locally
                                    __instance.PlayerJoinIndicators[num].ReadyEnabled();
                                    lobbyPlayer2.PlayerStatus = LobbyPlayer.Status.COUCH;
                                    
                                    // For local play, we can sit on the couch
                                    // For network play, sit locally first, then sync with server
                                    if (NetworkServer.active || !NetworkClient.active)
                                    {
                                        // Local game or server - we can sit on the couch
                                        __instance.HotSeatCouch.SitPlayer(player);
                                        Debug.Log($"[SpectatorMod] Local game: Sat player {spectatorPlayerNumber} on couch");
                                    }
                                    else
                                    {
                                        // Network client - sit locally immediately, then sync with server
                                        __instance.HotSeatCouch.SitPlayer(player);
                                        Debug.Log($"[SpectatorMod] Network client: Sat player {spectatorPlayerNumber} on couch locally, syncing with server");
                                        
                                        // Request server validation and sync
                                        RequestSpectatorSitdown(spectatorPlayerNumber);
                                    }
                                    
                                    // Network synchronization for spectator status
                                    if (!lobbyPlayer2.IsLocalPlayer)
                                    {
                                        Debug.Log($"[SpectatorMod] Network spectator: Player {spectatorPlayerNumber} (networkNumber: {lobbyPlayer2.networkNumber}) is networked - attempting sync");
                                        SyncSpectatorStatus(lobbyPlayer2.networkNumber, true);
                                    }
                                    else
                                    {
                                        Debug.Log($"[SpectatorMod] Local spectator: Player {spectatorPlayerNumber} is local");
                                    }
                                }
                                return false;
                            }
                        }
                    }
                }
                
                // Let the original method handle non-spectator logic
                return true;
            }
        }

        // Prevent cursor spawning for spectators
        [HarmonyPatch(typeof(InventoryBook), nameof(InventoryBook.AddPlayer))]
        static class InventoryBookAddPlayerPatch
        {
            static bool Prefix(InventoryBook __instance, int localPlayerNumber, int networkPlayerNumber, Controller input, Character.Animals animal)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return true; // Continue normally if spectator mode is disabled

                Debug.Log($"[SpectatorMod] InventoryBook.AddPlayer called for player {networkPlayerNumber}, spectator: {IsSpectator(networkPlayerNumber)}");
                
                if (IsSpectator(networkPlayerNumber))
                {
                    Debug.Log($"[SpectatorMod] Blocked cursor creation for spectator player {networkPlayerNumber}");
                    return false; // Don't add cursor for spectators
                }
                return true; // Continue normally
            }
        }

        // Remove spectators from scoreboard
        [HarmonyPatch(typeof(GraphScoreBoard), nameof(GraphScoreBoard.SetPlayerCount))]
        static class GraphScoreBoardSetPlayerCountPatch
        {
            static void Postfix(GraphScoreBoard __instance, int numberPlayers)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return; // Skip if spectator mode is disabled

                // Filter out spectators from score lines
                for (int i = 0; i < numberPlayers; i++)
                {
                    if (__instance.playerScoreLines[i] != null)
                    {
                        // Score lines are indexed by local player number (1-based)
                        // We need to find the corresponding network player number
                        int localPlayerNumber = i + 1;
                        bool isSpectator = false;
                        
                        // Find the network player number for this local player
                        if (LobbyManager.instance != null)
                        {
                            foreach (NetworkLobbyPlayer networkLobbyPlayer in LobbyManager.instance.lobbySlots)
                            {
                                if (networkLobbyPlayer != null)
                                {
                                    LobbyPlayer lobbyPlayer = networkLobbyPlayer as LobbyPlayer;
                                    if (lobbyPlayer != null && lobbyPlayer.localNumber == localPlayerNumber)
                                    {
                                        isSpectator = IsSpectator(lobbyPlayer.networkNumber);
                                        break;
                                    }
                                }
                            }
                        }
                        
                        if (isSpectator)
                        {
                            // Hide spectator score line
                            __instance.playerScoreLines[i].gameObject.SetActive(false);
                            Debug.Log($"[SpectatorMod] Hidden score line for spectator local player {localPlayerNumber}");
                        }
                    }
                }
            }
        }

        // Custom network message system for spectator status
        private const short SPECTATOR_STATUS_MSG_TYPE = 1000;
        
        // Network message class for spectator status updates
        public class SpectatorStatusMessage : MessageBase
        {
            public int networkPlayerNumber;
            public bool isSpectator;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(networkPlayerNumber);
                writer.Write(isSpectator);
            }

            public override void Deserialize(NetworkReader reader)
            {
                networkPlayerNumber = reader.ReadInt32();
                isSpectator = reader.ReadBoolean();
            }
        }
        
        // Patch VersusControl.SetupStart to handle spectators properly
        [HarmonyPatch(typeof(VersusControl), nameof(VersusControl.SetupStart))]
        static class VersusControlSetupStartPatch
        {
            static bool Prefix(VersusControl __instance, GameState.GameMode mode)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return true; // Continue normally if spectator mode is disabled
                
                // Null check to prevent crashes
                if (__instance == null || __instance.PlayerQueue == null)
                {
                    Debug.LogWarning("[SpectatorMod] VersusControl or PlayerQueue is null, skipping spectator filtering");
                    return true;
                }
                
                Debug.Log("[SpectatorMod] VersusControl.SetupStart called - checking for spectators");
                
                // Instead of replacing the queue, we'll filter it in place
                // This prevents null reference issues in the original code
                List<GamePlayer> spectatorsToRemove = new List<GamePlayer>();
                int activePlayerCount = 0;
                
                // First pass: identify spectators to remove
                foreach (GamePlayer gamePlayer in __instance.PlayerQueue)
                {
                    if (gamePlayer != null)
                    {
                        if (IsSpectator(gamePlayer.networkNumber))
                        {
                            spectatorsToRemove.Add(gamePlayer);
                            Debug.Log($"[SpectatorMod] Spectator player found for removal: {gamePlayer.networkNumber}");
                        }
                        else
                        {
                            activePlayerCount++;
                            Debug.Log($"[SpectatorMod] Active player found: {gamePlayer.networkNumber}, IsLocalPlayer: {gamePlayer.IsLocalPlayer}");
                        }
                    }
                }
                
                Debug.Log($"[SpectatorMod] Total players in queue: {__instance.PlayerQueue.Count}, Active players: {activePlayerCount}, Spectators to remove: {spectatorsToRemove.Count}");
                
                // Second pass: remove spectators by rebuilding the queue
                if (spectatorsToRemove.Count > 0)
                {
                    Queue<GamePlayer> newQueue = new Queue<GamePlayer>();
                    foreach (GamePlayer gamePlayer in __instance.PlayerQueue)
                    {
                        if (gamePlayer != null && !IsSpectator(gamePlayer.networkNumber))
                        {
                            newQueue.Enqueue(gamePlayer);
                        }
                    }
                    __instance.PlayerQueue = newQueue;
                    Debug.Log($"[SpectatorMod] Rebuilt PlayerQueue with {__instance.PlayerQueue.Count} active players");
                }
                
                return true; // Continue with original method using filtered queue
            }
            
            static void Postfix(VersusControl __instance, GameState.GameMode mode)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return;
                
                Debug.Log("[SpectatorMod] VersusControl.SetupStart completed");
            }
        }

        // Helper method to sync spectator status across network
        private static void SyncSpectatorStatus(int networkPlayerNumber, bool isSpectator)
        {
            Debug.Log($"[SpectatorMod] Syncing spectator status: Player {networkPlayerNumber} = {isSpectator}");
            
            // Update local state immediately
            SetSpectator(networkPlayerNumber, isSpectator);
            
            // Try to sync via LobbyPlayer status for networked players
            try
            {
                if (LobbyManager.instance != null)
                {
                    Debug.Log($"[SpectatorMod] LobbyManager available, attempting network sync for player {networkPlayerNumber}");
                    
                    // Find the LobbyPlayer for this network number
                    foreach (NetworkLobbyPlayer networkLobbyPlayer in LobbyManager.instance.lobbySlots)
                    {
                        if (networkLobbyPlayer != null)
                        {
                            LobbyPlayer lobbyPlayer = networkLobbyPlayer as LobbyPlayer;
                            if (lobbyPlayer != null && lobbyPlayer.networkNumber == networkPlayerNumber)
                            {
                                Debug.Log($"[SpectatorMod] Found LobbyPlayer for network sync: {lobbyPlayer.playerName} (networkNumber: {lobbyPlayer.networkNumber})");
                                
                                // Update the spectator status on the LobbyPlayer object
                                if (isSpectator)
                                {
                                    lobbyPlayer.PlayerStatus = LobbyPlayer.Status.COUCH;
                                    Debug.Log($"[SpectatorMod] Set network player {networkPlayerNumber} status to COUCH");
                                }
                                else
                                {
                                    lobbyPlayer.PlayerStatus = LobbyPlayer.Status.CHARACTER;
                                    Debug.Log($"[SpectatorMod] Set network player {networkPlayerNumber} status to CHARACTER");
                                }
                                break;
                            }
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("[SpectatorMod] LobbyManager.instance is null, cannot sync network spectator status");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpectatorMod] Exception during network sync: {ex.Message}");
            }
        }
        
        // Send spectator status update to all clients (using our custom network system)
        private static void SendSpectatorStatusUpdate(int networkPlayerNumber, bool isSpectator)
        {
            try
            {
                var msg = new SpectatorStatusMessage
                {
                    networkPlayerNumber = networkPlayerNumber,
                    isSpectator = isSpectator
                };
                
                // Send to all clients
                NetworkServer.SendToAll(SPECTATOR_STATUS_MSG_TYPE, msg);
                Debug.Log($"[SpectatorMod] Broadcast spectator status: Player {networkPlayerNumber} = {isSpectator}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpectatorMod] Failed to send spectator status update: {ex.Message}");
            }
        }
        
        // Hook into LobbyManager initialization for spectator setup
        [HarmonyPatch(typeof(LobbyManager), "OnStartClient")]
        static class LobbyManagerOnStartClientPatch
        {
            static void Postfix(LobbyManager __instance, NetworkClient lobbyClient)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return;
                    
                try
                {
                    Debug.Log("[SpectatorMod] LobbyManager OnStartClient - registering network handlers");
                    
                    // Register custom message handler for spectator status updates
                    if (NetworkServer.active)
                    {
                        NetworkServer.RegisterHandler(SPECTATOR_STATUS_MSG_TYPE, HandleSpectatorStatusMessage);
                        Debug.Log("[SpectatorMod] Registered spectator status message handler on server");
                    }
                    
                    if (__instance.client != null)
                    {
                        __instance.client.RegisterHandler(SPECTATOR_STATUS_MSG_TYPE, HandleSpectatorStatusMessage);
                        Debug.Log("[SpectatorMod] Registered spectator status message handler on client");
                    }
                    
                    Debug.Log("[SpectatorMod] Spectator network system ready");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SpectatorMod] Failed to setup spectator network system: {ex.Message}");
                }
            }
        }
        
        // Handle spectator status messages (robust network processing)
        private static void HandleSpectatorStatusMessage(NetworkMessage netMsg)
        {
            try
            {
                var msg = netMsg.ReadMessage<SpectatorStatusMessage>();
                
                if (NetworkServer.active)
                {
                    // Server-side: Process client requests and broadcast to all clients
                    if (netMsg.conn != null && netMsg.conn.address != "localServer")
                    {
                        Debug.Log($"[SpectatorMod] Server: Received spectator request - Player {msg.networkPlayerNumber} = {msg.isSpectator}");
                        
                        // Validate the request (server authority)
                        bool isValidRequest = ValidateSpectatorRequest(msg.networkPlayerNumber, msg.isSpectator, netMsg.conn);
                        
                        if (isValidRequest)
                        {
                            // Update server state and broadcast to all clients
                            spectatorPlayers[msg.networkPlayerNumber] = msg.isSpectator;
                            Debug.Log($"[SpectatorMod] Server: Updated player {msg.networkPlayerNumber} spectator status to {msg.isSpectator}");
                            
                            // Broadcast to all clients (including the sender)
                            NetworkServer.SendToAll(SPECTATOR_STATUS_MSG_TYPE, msg);
                            Debug.Log($"[SpectatorMod] Server: Broadcast spectator status for player {msg.networkPlayerNumber}");
                        }
                        else
                        {
                            Debug.LogWarning($"[SpectatorMod] Server: Rejected invalid spectator request from player {msg.networkPlayerNumber}");
                        }
                    }
                    // Ignore our own broadcasts to prevent feedback loop
                }
                else
                {
                    // Client-side: Apply spectator status from server
                    Debug.Log($"[SpectatorMod] Client: Received spectator status - Player {msg.networkPlayerNumber} = {msg.isSpectator}");
                    
                    // Update local state directly (no recursive calls)
                    spectatorPlayers[msg.networkPlayerNumber] = msg.isSpectator;
                    Debug.Log($"[SpectatorMod] Client: Updated player {msg.networkPlayerNumber} spectator status to {msg.isSpectator}");
                    
                    // Handle visual updates (couch sit/unsit)
                    var levelSelectController = LevelSelectController.lastInstance;
                    if (levelSelectController != null)
                    {
                        HandleClientSpectatorUpdate(levelSelectController, msg.networkPlayerNumber, msg.isSpectator);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpectatorMod] Error handling spectator status message: {ex.Message}");
            }
        }
        
        // Validate spectator requests on server (security and authority)
        private static bool ValidateSpectatorRequest(int networkPlayerNumber, bool isSpectator, NetworkConnection conn)
        {
            try
            {
                // Find the LobbyPlayer for this network number
                foreach (NetworkLobbyPlayer networkLobbyPlayer in LobbyManager.instance.lobbySlots)
                {
                    if (networkLobbyPlayer != null)
                    {
                        LobbyPlayer lobbyPlayer = networkLobbyPlayer as LobbyPlayer;
                        if (lobbyPlayer != null && lobbyPlayer.networkNumber == networkPlayerNumber)
                        {
                            // Verify that the request comes from the correct client
                            if (lobbyPlayer.connectionToClient == conn)
                            {
                                // Additional validation: check if player can become spectator
                                if (isSpectator)
                                {
                                    // Check if there's an available seat
                                    var levelSelectController = LevelSelectController.lastInstance;
                                    if (levelSelectController != null && levelSelectController.HotSeatCouch.IsSeatAvailable())
                                    {
                                        return true;
                                    }
                                    else
                                    {
                                        Debug.LogWarning($"[SpectatorMod] Server: No available spectator seats for player {networkPlayerNumber}");
                                        return false;
                                    }
                                }
                                else
                                {
                                    // Always allow leaving spectator mode
                                    return true;
                                }
                            }
                            else
                            {
                                Debug.LogWarning($"[SpectatorMod] Server: Connection mismatch for player {networkPlayerNumber}");
                                return false;
                            }
                        }
                    }
                }
                
                Debug.LogWarning($"[SpectatorMod] Server: Could not find player {networkPlayerNumber} for validation");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpectatorMod] Exception in ValidateSpectatorRequest: {ex.Message}");
                return false;
            }
        }
        
        // Handle spectator updates on clients (sit/unsit from couch)
        private static void HandleClientSpectatorUpdate(LevelSelectController controller, int networkPlayerNumber, bool isSpectator)
        {
            try
            {
                Debug.Log($"[SpectatorMod] Client handling spectator update: Player {networkPlayerNumber} = {isSpectator}");
                
                // get player by network number
                LobbyPlayer targetLobbyPlayer = controller.FindLobbyPlayer(networkPlayerNumber);
                                
                // Only proceed if we found the correct lobby player
                if (targetLobbyPlayer != null)
                {
                    // Get the actual player from the lobby player (more reliable)
                    Player player = targetLobbyPlayer.LocalPlayer;
                    
                    if (player != null)
                    {
                        // Additional check: only apply spectator changes if this player is actually meant to be a spectator
                        bool shouldBeSpectator = IsSpectator(networkPlayerNumber);
                        
                        if (shouldBeSpectator == isSpectator)
                        {
                            if (isSpectator)
                            {
                                // Sit player on couch
                                Debug.Log($"[SpectatorMod] Client sitting player {networkPlayerNumber} on couch (confirmed spectator)");
                                controller.HotSeatCouch.SitPlayer(player);
                            }
                            else
                            {
                                // Unsit player from couch
                                Debug.Log($"[SpectatorMod] Client unsitting player {networkPlayerNumber} from couch (confirmed non-spectator)");
                                controller.HotSeatCouch.UnsitPlayer(player);
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"[SpectatorMod] Skipping couch operation for player {networkPlayerNumber} - spectator status mismatch: expected {shouldBeSpectator}, got {isSpectator}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[SpectatorMod] Could not find LocalPlayer for lobby player {networkPlayerNumber}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[SpectatorMod] Could not find lobby player {networkPlayerNumber} for couch operations");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpectatorMod] Error in HandleClientSpectatorUpdate: {ex.Message}");
            }
        }

        // Handle player disconnect to clean up spectator status
        [HarmonyPatch(typeof(LobbyManager), "OnClientDisconnect")]
        static class LobbyManagerOnClientDisconnectPatch
        {
            static void Postfix(NetworkConnection conn)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return;
                    
                try
                {
                    // Find the player that disconnected and clean up their spectator status
                    foreach (NetworkLobbyPlayer networkLobbyPlayer in LobbyManager.instance.lobbySlots)
                    {
                        if (networkLobbyPlayer != null && networkLobbyPlayer.connectionToClient == conn)
                        {
                            LobbyPlayer lobbyPlayer = networkLobbyPlayer as LobbyPlayer;
                            if (lobbyPlayer != null)
                            {
                                Debug.Log($"[SpectatorMod] Player {lobbyPlayer.networkNumber} disconnected, cleaning up spectator status");
                                
                                // Remove spectator status for disconnected player
                                if (spectatorPlayers.ContainsKey(lobbyPlayer.networkNumber))
                                {
                                    spectatorPlayers.Remove(lobbyPlayer.networkNumber);
                                }
                                
                                // Notify other clients about the status change
                                if (NetworkServer.active)
                                {
                                    SendSpectatorStatusUpdate(lobbyPlayer.networkNumber, false);
                                }
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SpectatorMod] Error handling player disconnect: {ex.Message}");
                }
            }
        }
        // Sync all spectator status to newly connected clients (using our message system)
        [HarmonyPatch(typeof(LobbyManager), "OnLobbyServerConnect")]
        static class LobbyManagerOnLobbyServerConnectPatch
        {
            static void Postfix(LobbyManager __instance, NetworkConnection conn)
            {
                if (!MorePlayersMod.spectatorMode.Value || !NetworkServer.active)
                    return;
                    
                // Only sync if there are actually spectators to avoid unnecessary traffic
                bool hasSpectators = false;
                foreach (var kvp in spectatorPlayers)
                {
                    if (kvp.Value)
                    {
                        hasSpectators = true;
                        break;
                    }
                }
                
                if (!hasSpectators)
                    return; // No spectators, no need to sync
                    
                try
                {
                    Debug.Log($"[SpectatorMod] Syncing existing spectator status to new client");
                    
                    // Send current spectator status for all spectators to the new client
                    foreach (var kvp in spectatorPlayers)
                    {
                        if (kvp.Value) // Only send for spectators
                        {
                            var msg = new SpectatorStatusMessage
                            {
                                networkPlayerNumber = kvp.Key,
                                isSpectator = true
                            };
                            
                            // Send only to the new client (not all clients)
                            conn.Send(SPECTATOR_STATUS_MSG_TYPE, msg);
                            Debug.Log($"[SpectatorMod] Synced spectator status for player {kvp.Key} to new client");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SpectatorMod] Error syncing spectator status to new client: {ex.Message}");
                }
            }
        }

        // Block UnpickCharacter for recent spectators to prevent cursor creation
        [HarmonyPatch(typeof(LobbyPlayer), nameof(LobbyPlayer.UnpickCharacter))]
        static class LobbyPlayerUnpickCharacterPatch
        {
            static bool Prefix(LobbyPlayer __instance)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return true; // Continue normally if spectator mode is disabled

                // Block UnpickCharacter if this player was recently a spectator (within last 3 seconds)
                if (IsRecentSpectator(__instance.networkNumber))
                {
                    Debug.Log($"[SpectatorMod] Blocked UnpickCharacter for recent spectator player {__instance.networkNumber}");
                    return false; // Block UnpickCharacter to prevent cursor creation
                }
                return true; // Continue normally
            }
        }
    }
}
