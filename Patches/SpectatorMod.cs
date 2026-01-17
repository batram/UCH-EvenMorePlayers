using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;
using UnityEngine.UI;
using System;
using System.Collections;

namespace MorePlayers
{
    [HarmonyPatch]
    static class SpectatorModPatches
    {
        // Custom message type for spectator status updates
        private const short SPECTATOR_STATUS_MSG_TYPE = 1000;
        
        // Static dictionary to track spectator players
        private static readonly Dictionary<int, bool> spectatorPlayers = new Dictionary<int, bool>();
        
        // Network message class for spectator status updates
        public class SpectatorStatusMessage : MessageBase
        {
            public int playerNumber;
            public bool isSpectator;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(playerNumber);
                writer.Write(isSpectator);
            }

            public override void Deserialize(NetworkReader reader)
            {
                playerNumber = reader.ReadInt32();
                isSpectator = reader.ReadBoolean();
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
        private static bool IsSpectator(int playerNumber)
        {
            return spectatorPlayers.ContainsKey(playerNumber) && spectatorPlayers[playerNumber];
        }

        // Helper method to check if player was recently a spectator (within last 3 seconds)
        private static bool IsRecentSpectator(int playerNumber)
        {
            if (!recentSpectators.ContainsKey(playerNumber))
                return false;
            
            float timeSinceExit = Time.time - recentSpectators[playerNumber];
            return timeSinceExit < 3f; // 3 seconds
        }

        // Helper method to check if player recently exited spectator mode (within 1 second)
        private static bool RecentlyExitedSpectator(int playerNumber)
        {
            if (!lastSpectatorExit.ContainsKey(playerNumber))
                return false;
            
            float timeSinceExit = Time.time - lastSpectatorExit[playerNumber];
            return timeSinceExit < 1f; // 1 second
        }

        // Helper method to request spectator sitdown from server (client-side)
        private static void RequestSpectatorSitdown(int playerNumber)
        {
            try
            {
                if (LobbyManager.instance != null && NetworkClient.active)
                {
                    Debug.Log($"[SpectatorMod] Client requesting spectator sitdown for player {playerNumber}");
                    
                    // Create spectator status message to send to server
                    SpectatorStatusMessage msg = new SpectatorStatusMessage
                    {
                        playerNumber = playerNumber,
                        isSpectator = true
                    };
                    
                    // Send to server using the client connection
                    NetworkClient.allClients[0].Send(SPECTATOR_STATUS_MSG_TYPE, msg);
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

        // Helper method to set spectator status
        private static void SetSpectator(int playerNumber, bool isSpectator)
        {
            spectatorPlayers[playerNumber] = isSpectator;
            Debug.Log($"[SpectatorMod] Player {playerNumber} spectator status set to: {isSpectator}");
            
            // Network sync - send to all clients if we're server
            if (NetworkServer.active)
            {
                SendSpectatorStatusUpdate(playerNumber, isSpectator);
            }
            else if (isSpectator)
            {
                // If we're a client and becoming spectator, request sitdown from server
                RequestSpectatorSitdown(playerNumber);
            }
            
            // Track when spectator exits
            if (!isSpectator)
            {
                recentSpectators[playerNumber] = Time.time;
                lastSpectatorExit[playerNumber] = Time.time;
                Debug.Log($"[SpectatorMod] Player {playerNumber} marked as recent spectator at {Time.time}");
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
                            Debug.Log($"[SpectatorMod] Checking player {num}: Status={lobbyPlayer2.PlayerStatus}, IsSpectator={IsSpectator(num + 1)}, LocalPlayer={lobbyPlayer2.LocalPlayer != null}");
                            
                            // Check if this is a spectator sitting on the couch
                            bool isSpectatorStatus = lobbyPlayer2.PlayerStatus == LobbyPlayer.Status.COUCH;
                            bool isSpectator = IsSpectator(num + 1);
                            bool isSitting = lobbyPlayer2.LocalPlayer != null && __instance.HotSeatCouch.PlayerSitting(lobbyPlayer2.LocalPlayer);
                            
                            Debug.Log($"[SpectatorMod] Exit conditions: COUCH={isSpectatorStatus}, IsSpectator={isSpectator}, PlayerSitting={isSitting}");
                            
                            if (isSpectatorStatus && isSpectator && isSitting)
                            {
                                Debug.Log($"[SpectatorMod] Player {num + 1} attempting to leave spectator mode via {e.Key}");
                                
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
                                SetSpectator(num + 1, false);
                                
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
                                Character playerCharacter = lobbyPlayer2.characterInstance;
                                Player player = lobbyPlayer2.LocalPlayer;
                                
                                if (playerCharacter == null)
                                {
                                    Debug.LogError($"[SpectatorMod] ERROR: Could not find player for spectator entry. localNumber={lobbyPlayer2.localNumber}, networkNumber={lobbyPlayer2.networkNumber}");
                                    return false;
                                }
                                
                                // Check if character is at couch and not recently exited spectator
                                bool characterAtCouch = playerCharacter != null && __instance.HotSeatCouch.CharacterAtCouch(playerCharacter);
                                bool characterInMenu = playerCharacter != null && playerCharacter.InMenu;
                                int spectatorPlayerNumber = (player != null) ? player.Number : (num + 1);
                                bool notRecentlyExited = !RecentlyExitedSpectator(spectatorPlayerNumber);
                                
                                Debug.Log($"[SpectatorMod] Entry conditions: CharacterAtCouch={characterAtCouch}, NotInMenu={!characterInMenu}, NotRecentlyExited={notRecentlyExited}");
                                
                                if (characterAtCouch && !characterInMenu && notRecentlyExited && player != null)
                                {
                                    Debug.Log($"[SpectatorMod] Player {spectatorPlayerNumber} becoming spectator");
                                    
                                    // Set spectator status and handle network sync
                                    SetSpectator(spectatorPlayerNumber, true);
                                    
                                    // Update player status locally
                                    __instance.PlayerJoinIndicators[num].ReadyEnabled();
                                    lobbyPlayer2.PlayerStatus = LobbyPlayer.Status.COUCH;
                                    
                                    // For local play, we can sit on the couch
                                    // For network play, each client will handle their own visual representation
                                    if (NetworkServer.active || !NetworkClient.active)
                                    {
                                        // Local game or server - we can sit on the couch
                                        __instance.HotSeatCouch.SitPlayer(player);
                                        Debug.Log($"[SpectatorMod] Local game: Sat player {spectatorPlayerNumber} on couch");
                                    }
                                    else
                                    {
                                        // Network client - spectator status will be synced via network
                                        // Visual representation will be handled by each client receiving the sync
                                        Debug.Log($"[SpectatorMod] Network client: Spectator status will be synced via network");
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

                Debug.Log($"[SpectatorMod] InventoryBook.AddPlayer called for player {localPlayerNumber}, spectator: {IsSpectator(localPlayerNumber)}");
                
                if (IsSpectator(localPlayerNumber))
                {
                    Debug.Log($"[SpectatorMod] Blocked cursor creation for spectator player {localPlayerNumber}");
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
                        if (IsSpectator(i + 1))
                        {
                            // Hide spectator score line
                            __instance.playerScoreLines[i].gameObject.SetActive(false);
                        }
                    }
                }
            }
        }

        // Ensure spectators can see the game but don't interfere
        [HarmonyPatch(typeof(GameControl), nameof(GameControl.ReceiveEvent))]
        static class GameControlReceiveEventPatch
        {
            static bool Prefix(GameControl __instance, InputEvent e)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return true; // Continue normally if spectator mode is disabled

                // Check if this input is from a spectator
                for (int i = 0; i < PlayerManager.maxPlayers; i++)
                {
                    if ((e.PlayerBitMask & (1 << i)) == (1 << i))
                    {
                        if (IsSpectator(i + 1))
                        {
                            return false; // Block input from spectators
                        }
                    }
                }
                return true; // Allow normal input
            }
        }

        // Handle spectator network synchronization
        [HarmonyPatch(typeof(LobbyPlayer), nameof(LobbyPlayer.CmdPlayerPickedCharacter))]
        static class LobbyPlayerCmdPlayerPickedCharacterPatch
        {
            static void Postfix(LobbyPlayer __instance, Character.Animals animal, bool clearOutfit)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return; // Skip if spectator mode is disabled

                if (__instance.PlayerStatus == LobbyPlayer.Status.COUCH && IsSpectator(__instance.networkNumber))
                {
                    // Ensure spectator status is synchronized
                    __instance.PlayerStatus = LobbyPlayer.Status.COUCH;
                    
                    // Sync spectator status to all clients
                    SyncSpectatorStatus(__instance.networkNumber, true);
                }
            }
        }
        
        // Network command to sync spectator status
        [HarmonyPatch(typeof(LobbyPlayer), "RpcRequestPickResponse")]
        static class LobbyPlayerRpcRequestPickResponsePatch
        {
            static void Postfix(LobbyPlayer __instance, int playerNetworkNumber, bool response)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return; // Skip if spectator mode is disabled
                    
                // When a player becomes spectator, update their status on all clients
                if (response && __instance.PlayerStatus == LobbyPlayer.Status.COUCH)
                {
                    SetSpectator(playerNetworkNumber, true);
                    Debug.Log($"[SpectatorMod] Network: Player {playerNetworkNumber} became spectator");
                }
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
                
                Debug.Log("[SpectatorMod] VersusControl.SetupStart called - checking for spectators");
                
                // Count active players (excluding spectators) BEFORE the original method runs
                int activePlayerCount = 0;
                Queue<GamePlayer> filteredQueue = new Queue<GamePlayer>();
                
                // Filter out spectators from the PlayerQueue
                foreach (GamePlayer gamePlayer in __instance.PlayerQueue)
                {
                    if (gamePlayer != null && !IsSpectator(gamePlayer.networkNumber))
                    {
                        activePlayerCount++;
                        filteredQueue.Enqueue(gamePlayer);
                        Debug.Log($"[SpectatorMod] Active player found: {gamePlayer.networkNumber}, IsLocalPlayer: {gamePlayer.IsLocalPlayer}");
                    }
                    else if (gamePlayer != null && IsSpectator(gamePlayer.networkNumber))
                    {
                        Debug.Log($"[SpectatorMod] Spectator player found and will be excluded: {gamePlayer.networkNumber}");
                    }
                }
                
                Debug.Log($"[SpectatorMod] Total players in queue: {__instance.PlayerQueue.Count}, Active players: {activePlayerCount}");
                
                // If we have spectators, replace the PlayerQueue with filtered version
                if (activePlayerCount < __instance.PlayerQueue.Count)
                {
                    Debug.Log("[SpectatorMod] Spectators detected - replacing PlayerQueue with filtered version");
                    __instance.PlayerQueue = filteredQueue;
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
        private static void SyncSpectatorStatus(int playerNumber, bool isSpectator)
        {
            Debug.Log($"[SpectatorMod] Syncing spectator status: Player {playerNumber} = {isSpectator}");
            
            // Update local state immediately
            SetSpectator(playerNumber, isSpectator);
            
            // Try to sync via LobbyPlayer status for networked players
            try
            {
                if (LobbyManager.instance != null)
                {
                    Debug.Log($"[SpectatorMod] LobbyManager available, attempting network sync for player {playerNumber}");
                    
                    // Find the LobbyPlayer for this network number
                    foreach (NetworkLobbyPlayer networkLobbyPlayer in LobbyManager.instance.lobbySlots)
                    {
                        if (networkLobbyPlayer != null)
                        {
                            LobbyPlayer lobbyPlayer = networkLobbyPlayer as LobbyPlayer;
                            if (lobbyPlayer != null && lobbyPlayer.networkNumber == playerNumber)
                            {
                                Debug.Log($"[SpectatorMod] Found LobbyPlayer for network sync: {lobbyPlayer.playerName} (networkNumber: {lobbyPlayer.networkNumber})");
                                
                                // Update the spectator status on the LobbyPlayer object
                                if (isSpectator)
                                {
                                    lobbyPlayer.PlayerStatus = LobbyPlayer.Status.COUCH;
                                    Debug.Log($"[SpectatorMod] Set network player {playerNumber} status to COUCH");
                                }
                                else
                                {
                                    lobbyPlayer.PlayerStatus = LobbyPlayer.Status.CHARACTER;
                                    Debug.Log($"[SpectatorMod] Set network player {playerNumber} status to CHARACTER");
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
        
        // Send spectator status update to all clients
        private static void SendSpectatorStatusUpdate(int playerNumber, bool isSpectator)
        {
            try
            {
                var msg = new SpectatorStatusMessage
                {
                    playerNumber = playerNumber,
                    isSpectator = isSpectator
                };
                
                // Use a custom message type that won't conflict with existing ones
                // We'll use a high number to avoid conflicts
                const short SPECTATOR_STATUS_MSG_TYPE = 1000;
                
                NetworkServer.SendToAll(SPECTATOR_STATUS_MSG_TYPE, msg);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpectatorMod] Failed to send spectator status update: {ex.Message}");
            }
        }
        
        // Hook into LobbyManager initialization to register our message handler
        [HarmonyPatch(typeof(LobbyManager), "OnStartClient")]
        static class LobbyManagerOnStartClientPatch
        {
            static void Postfix(LobbyManager __instance, NetworkClient lobbyClient)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return;
                    
                try
                {
                    // Register custom message handler for spectator status updates
                    const short SPECTATOR_STATUS_MSG_TYPE = 1000;
                    
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
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SpectatorMod] Failed to register message handler: {ex.Message}");
                }
            }
        }
        
        // Handle spectator status messages
        private static void HandleSpectatorStatusMessage(NetworkMessage netMsg)
        {
            try
            {
                var msg = netMsg.ReadMessage<SpectatorStatusMessage>();
                
                if (NetworkServer.active)
                {
                    // Server-side: Only process messages from clients, not our own broadcasts
                    if (netMsg.conn != null && netMsg.conn.address != "localServer")
                    {
                        Debug.Log($"[SpectatorMod] Server: Player {msg.playerNumber} spectator status = {msg.isSpectator}");
                        
                        // Update server state
                        spectatorPlayers[msg.playerNumber] = msg.isSpectator;
                        
                        // Broadcast to all clients (including the sender)
                        NetworkServer.SendToAll(SPECTATOR_STATUS_MSG_TYPE, msg);
                    }
                    // Ignore our own broadcasts to prevent feedback loop
                }
                else
                {
                    // Client-side: Apply the spectator status and handle couch operations
                    Debug.Log($"[SpectatorMod] Client: Player {msg.playerNumber} spectator status = {msg.isSpectator}");
                    
                    // Update local state
                    spectatorPlayers[msg.playerNumber] = msg.isSpectator;
                    
                    // Find the LevelSelectController to handle couch operations
                    var levelSelectController = LevelSelectController.lastInstance;
                    if (levelSelectController != null)
                    {
                        HandleClientSpectatorUpdate(levelSelectController, msg.playerNumber, msg.isSpectator);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpectatorMod] Error handling spectator status message: {ex.Message}");
            }
        }
        
        // Handle spectator updates on clients (sit/unsit from couch)
        private static void HandleClientSpectatorUpdate(LevelSelectController controller, int playerNumber, bool isSpectator)
        {
            try
            {
                Debug.Log($"[SpectatorMod] Client handling spectator update: Player {playerNumber} = {isSpectator}");
                
                // Find the player and lobby player
                Player player = PlayerManager.GetInstance().GetPlayer(playerNumber);
                LobbyPlayer lobbyPlayer = null;
                
                // Find the corresponding LobbyPlayer
                if (LobbyManager.instance != null)
                {
                    foreach (NetworkLobbyPlayer networkLobbyPlayer in LobbyManager.instance.lobbySlots)
                    {
                        if (networkLobbyPlayer != null)
                        {
                            LobbyPlayer lp = networkLobbyPlayer as LobbyPlayer;
                            if (lp != null && (lp.networkNumber == playerNumber || lp.localNumber == playerNumber))
                            {
                                lobbyPlayer = lp;
                                break;
                            }
                        }
                    }
                }
                
                if (player != null && lobbyPlayer != null)
                {
                    if (isSpectator)
                    {
                        // Sit player on couch
                        Debug.Log($"[SpectatorMod] Client sitting player {playerNumber} on couch");
                        controller.HotSeatCouch.SitPlayer(player);
                        controller.PlayerJoinIndicators[playerNumber - 1].ReadyEnabled();
                        lobbyPlayer.PlayerStatus = LobbyPlayer.Status.COUCH;
                    }
                    else
                    {
                        // Unsit player from couch
                        Debug.Log($"[SpectatorMod] Client unsitting player {playerNumber} from couch");
                        controller.HotSeatCouch.UnsitPlayer(player);
                        controller.PlayerJoinIndicators[playerNumber - 1].PickLevelEnabled();
                        lobbyPlayer.PlayerStatus = LobbyPlayer.Status.CHARACTER;
                    }
                }
                else
                {
                    Debug.LogWarning($"[SpectatorMod] Could not find player {playerNumber} or lobby player for couch operations");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpectatorMod] Error in HandleClientSpectatorUpdate: {ex.Message}");
            }
        }
        
        // Sync spectator status when players initialize
        [HarmonyPatch(typeof(LobbyPlayer), "FindLobbyObjects")]
        static class LobbyPlayerFindLobbyObjectsPatch
        {
            static void Postfix(LobbyPlayer __instance)
            {
                // Removed automatic sync requests - only sync on actual status changes
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
        
        // Sync all spectator status to newly connected clients (only if there are spectators)
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
                    // Send current spectator status for all spectators to the new client
                    foreach (var kvp in spectatorPlayers)
                    {
                        if (kvp.Value) // Only send for spectators
                        {
                            var msg = new SpectatorStatusMessage
                            {
                                playerNumber = kvp.Key,
                                isSpectator = true
                            };
                            
                            const short SPECTATOR_STATUS_MSG_TYPE = 1000;
                            conn.Send(SPECTATOR_STATUS_MSG_TYPE, msg);
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
