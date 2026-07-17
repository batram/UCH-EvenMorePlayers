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
        public static readonly Dictionary<int, bool> spectatorPlayers = new Dictionary<int, bool>();
        
        // Cached SpectatorHotSeat instance
        private static SpectatorHotSeat SpectatorHotSeatInstance;
        
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
        
        
        // Reset couch customization when lobby is destroyed
        [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.OnDestroy))]
        static class LobbyManagerOnDestroyPatch
        {
            static void Prefix()
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return;

                Debug.Log("[SpectatorMod] Lobby destroyed - preserving SpectatorHotSeatInstance for next session");
                // Don't destroy SpectatorHotSeatInstance - we want to preserve it across lobby transitions
                // SpectatorHotSeatInstance will persist and be reused when returning to treehouse
            }
        }
        
        // Static flag to prevent rapid re-entry (within 1 second)
        private static readonly Dictionary<int, float> lastSpectatorExit = new Dictionary<int, float>();

        // Helper method to check if player is spectator
        public static bool IsSpectator(int networkPlayerNumber)
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
                    Debug.Log($"[SpectatorMod] Client requesting spectator sitdown for player {playerNumber} (NetworkClient.active: {NetworkClient.active}, NetworkServer.active: {NetworkServer.active})");
                    
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
            
            // For local games, handle directly without network messages
            if (!LobbyManager.instance.IsInOnlineGame)
            {
                Debug.Log($"[SpectatorMod] Local game - handling spectator change directly");
                HandleLocalSpectatorChange(networkPlayerNumber, isSpectator);
            }
            else
            {
                // Network sync for online games
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
            }
            
            // Track when spectator exits
            if (!isSpectator)
            {
                recentSpectators[networkPlayerNumber] = Time.time;
                lastSpectatorExit[networkPlayerNumber] = Time.time;
                Debug.Log($"[SpectatorMod] Player {networkPlayerNumber} marked as recent spectator at {Time.time}");
            }
        }

        // Handle spectator changes directly in local games (no network)
        private static void HandleLocalSpectatorChange(int networkPlayerNumber, bool isSpectator)
        {
            var levelSelectController = LevelSelectController.lastInstance;
            if (levelSelectController == null || SpectatorHotSeatInstance == null)
                return;

            // Find the lobby player
            LobbyPlayer lobbyPlayer = levelSelectController.FindLobbyPlayer(networkPlayerNumber);
            if (lobbyPlayer == null) return;

            // Get the player
            Player player = PlayerManager.GetInstance().GetPlayer(lobbyPlayer.localNumber);
            if (player == null) return;

            if (isSpectator)
            {
                Debug.Log($"[SpectatorMod] Local: Sitting player {networkPlayerNumber} on couch");
                SpectatorHotSeatInstance.SitPlayer(player);
            }
            else
            {
                Debug.Log($"[SpectatorMod] Local: Unsitting player {networkPlayerNumber} from couch");
                SpectatorHotSeatInstance.UnsitPlayer(player);
            }
        }

        // Show couch in online mode for spectator functionality
        [HarmonyPatch(typeof(HotSeat), nameof(HotSeat.Update))]
        static class HotSeatUpdatePatch
        {
            static bool Prefix(HotSeat __instance)
            {
                if (LobbyManager.instance != null && MorePlayersMod.spectatorMode.Value && SpectatorHotSeatInstance == null)
                {
                    // Always show couch, even in online mode
                    __instance.show();
                    
                    Debug.Log("[SpectatorHotSeat] Replacing HotSeat component with SpectatorHotSeat");
                    SpectatorHotSeatInstance = __instance.gameObject.AddComponent<SpectatorHotSeat>();
                    
                    // Copy seat positions from original component
                    SpectatorHotSeatInstance.SeatPositions = __instance.SeatPositions;
                    
                    // Apply styling to the new component
                    SpectatorHotSeatInstance.ApplySpectatorCouchStyling();
                    
                    UnityEngine.Object.Destroy(__instance.GetComponent<HotSeat>());

                    
                    return false; // Skip original method
                }
                return true; // Continue with original method if spectator mode is disabled
            }
        }

        // Helper method to find lobby player for character
        private static LobbyPlayer FindLobbyPlayerForCharacter(Character character)
        {
            if (LevelSelectController.lastInstance != null)
            {
                foreach (LobbyPlayer lobbyPlayer in LevelSelectController.lastInstance.JoinedPlayers)
                {
                    if (lobbyPlayer != null && lobbyPlayer.CharacterInstance == character)
                    {
                        return lobbyPlayer;
                    }
                }
            }
            return null;
        }

        // Hook the original couch sitdown logic to handle spectator mode
        [HarmonyPatch(typeof(LevelSelectController), "ReceiveEvent")]
        static class LevelSelectControllerReceiveEventPatch
        {
            static bool Prefix(LevelSelectController __instance, InputEvent e)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return true;

                int controlMask = e.Sender.GetControlMask();
                
                LobbyPlayer localPlayer = FindLocalPlayerForInput(e.Sender, controlMask);

                
                // Handle spectator entry (Accept button)
                if ((!e.Sender.IsKeyboard || !Controller.InputFieldWasActiveRecently) && e.Key == InputEvent.InputKey.Accept && e.Valueb && e.Changed)
                {
                    // Find the LOCAL player that triggered this input
                    if (localPlayer != null && localPlayer.PlayerStatus == LobbyPlayer.Status.CHARACTER)
                    {
                        Debug.Log($"[SpectatorMod] Local player {localPlayer.networkNumber} entering spectator mode");
                        
                        // Get the actual player character
                        Player player = PlayerManager.GetInstance().GetPlayer(localPlayer.localNumber);
                        Character playerCharacter = player?.PlayerCharacter;
                        
                        if (SpectatorHotSeatInstance != null && playerCharacter != null && SpectatorHotSeatInstance.CharacterAtCouch(playerCharacter) && 
                            !playerCharacter.InMenu && !RecentlyExitedSpectator(localPlayer.networkNumber))
                        {
                            SpectatorHotSeatInstance.SitPlayer(player);
                            
                            // Set spectator status (this handles network sync automatically)
                            SetSpectator(localPlayer.networkNumber, true);
                            
                            return false; // Prevent original method
                        }
                    }
                }

                // Let the original method handle non-spectator logic
                return true;
            }
            
            // Helper method to find the LOCAL player for input
            private static LobbyPlayer FindLocalPlayerForInput(InputMethod sender, int controlMask)
            {
                if (controlMask == 0) return null;
                
                foreach (LobbyPlayer lobbyPlayer in LevelSelectController.lastInstance?.JoinedPlayers)
                {
                    if (lobbyPlayer != null && lobbyPlayer.IsLocalPlayer && sender.ControlsPlayer(lobbyPlayer.localNumber))
                    {
                        return lobbyPlayer;
                    }
                }
                return null;
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
                Debug.Log($"[SpectatorMod] Network message received: Player {msg.networkPlayerNumber} = {msg.isSpectator}, Connection: {netMsg.conn?.address}, IsServer: {NetworkServer.active}");
                
                if (NetworkServer.active)
                {
                    // Server-side: Process client requests (don't rebroadcast to avoid loop)
                    if (netMsg.conn != null && netMsg.conn.address != "localServer")
                    {
                        Debug.Log($"[SpectatorMod] Server: Received spectator request - Player {msg.networkPlayerNumber} = {msg.isSpectator}");
                        
                        // Update server state only (no broadcast to avoid infinite loop)
                        spectatorPlayers[msg.networkPlayerNumber] = msg.isSpectator;
                        
                        // Server handles sit/unsit logic directly
                        var levelSelectController = LevelSelectController.lastInstance;
                        if (levelSelectController != null)
                        {
                            HandleServerSpectatorUpdate(levelSelectController, msg.networkPlayerNumber, msg.isSpectator);
                        }
                    }
                }
                else
                {
                    // Client-side: Apply spectator status from server
                    Debug.Log($"[SpectatorMod] Client: Received spectator status - Player {msg.networkPlayerNumber} = {msg.isSpectator}");
                    
                    // Update local state directly (no recursive calls)
                    spectatorPlayers[msg.networkPlayerNumber] = msg.isSpectator;
                    Debug.Log($"[SpectatorMod] Client: Updated player {msg.networkPlayerNumber} spectator status to {msg.isSpectator}");
                    
                    // Clients do NOT execute sit/unsit logic - only server handles this
                    // This prevents multiple clients from trying to unsit the same player
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpectatorMod] Error handling spectator status message: {ex.Message}");
            }
        }
                
        // Handle spectator updates on server (sit/unsit from couch)
        private static void HandleServerSpectatorUpdate(LevelSelectController controller, int networkPlayerNumber, bool isSpectator)
        {
            try
            {
                Debug.Log($"[SpectatorMod] Server handling spectator update: Player {networkPlayerNumber} = {isSpectator}");
                
                // get player by network number
                LobbyPlayer targetLobbyPlayer = controller.FindLobbyPlayer(networkPlayerNumber);
                                
                // Only proceed if we found the correct lobby player
                if (targetLobbyPlayer != null)
                {
                    // Get the actual player from the lobby player (use localNumber for PlayerManager lookup)
                    Player player = PlayerManager.GetInstance().GetPlayer(targetLobbyPlayer.localNumber);
                    
                    if (player != null)
                    {
                        // Additional check: only apply spectator changes if this player is actually meant to be a spectator
                        bool shouldBeSpectator = IsSpectator(networkPlayerNumber);
                        
                        if (shouldBeSpectator == isSpectator)
                        {
                            if (isSpectator)
                            {
                                // Sit player on couch
                                Debug.Log($"[SpectatorMod] Server sitting player {networkPlayerNumber} on couch (confirmed spectator)");
                                SpectatorHotSeatInstance.SitPlayer(player);
                            }
                            else
                            {
                                // Unsit player from couch only if they're actually seated
                                if (SpectatorHotSeatInstance != null && SpectatorHotSeatInstance.PlayerSitting(player))
                                {
                                    Debug.Log($"[SpectatorMod] Server unsitting player {networkPlayerNumber} from couch (confirmed non-spectator)");
                                    SpectatorHotSeatInstance.UnsitPlayer(player);
                                }
                                else
                                {
                                    Debug.Log($"[SpectatorMod] Server skipping unsit for player {networkPlayerNumber} - not seated on couch");
                                }
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

                // Block UnpickCharacter if this player was recently a spectator (within last 3 seconds) or is currently a spectator
                if (IsRecentSpectator(__instance.networkNumber) || IsSpectator(__instance.networkNumber))
                {
                    Debug.Log($"[SpectatorMod] Blocked UnpickCharacter for recent spectator player {__instance.networkNumber}");
                    return false; // Block UnpickCharacter to prevent cursor creation
                }
                return true; // Continue normally
            }
        }

        // Single patch to filter PlayerQueue once after SetupStart completes
        [HarmonyPatch(typeof(GameControl), nameof(GameControl.SetupStart))]
        static class GameControlSetupStartPostfixPatch
        {
            static void Postfix(GameControl __instance, GameState.GameMode mode)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return;

                try
                {
                    int originalCount = __instance.PlayerQueue.Count;
                    if (originalCount == 0)
                        return;

                    // Filter spectators out of PlayerQueue once
                    Queue<GamePlayer> filteredQueue = new Queue<GamePlayer>();
                    GamePlayer[] players = __instance.PlayerQueue.ToArray();

                    foreach (GamePlayer player in players)
                    {
                        if (!IsSpectator(player.networkNumber))
                        {
                            filteredQueue.Enqueue(player);
                        }
                        else
                        {
                            Debug.Log($"[SpectatorMod] Filtered spectator {player.networkNumber} from PlayerQueue");
                        }
                    }

                    // Replace queue with filtered version
                    __instance.PlayerQueue = filteredQueue;
                    Debug.Log($"[SpectatorMod] Filtered PlayerQueue: {originalCount} -> {filteredQueue.Count} players");

                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[SpectatorMod] Error filtering PlayerQueue: {ex.Message}");
                }
            }
        }

        // Patch to setup spectator cursors when game starts
        [HarmonyPatch(typeof(VersusControl), nameof(VersusControl.SetupStart))]
        static class VersusControlSetupStartPatch
        {
            static void Postfix(VersusControl __instance, GameState.GameMode mode)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return;

                try
                {
                    if (__instance.invBookInstance?.pickCursorPrefab == null)
                    {
                        Debug.LogWarning("[SpectatorMod] InventoryBook or cursor prefab not found");
                        return;
                    }

                    // Create cursors for all spectators
                    PlayerManager playerManager = PlayerManager.GetInstance();
                    if (playerManager == null)
                        return;

                    foreach (Player player in playerManager)
                    {
                        if (player?.AssociatedLobbyPlayer != null && IsSpectator(player.AssociatedLobbyPlayer.networkNumber))
                        {
                            CreateSpectatorCursor(player, player.AssociatedLobbyPlayer.networkNumber, __instance.invBookInstance.pickCursorPrefab);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[SpectatorMod] Error setting up spectator cursors: {ex.Message}");
                }
            }
        }


        // Patch to handle spectator input (exit spectator mode and menu access)
        [HarmonyPatch(typeof(Controller), nameof(Controller.Notify))]
        static class ControllerNotifyPatch
        {
            static bool Prefix(Controller __instance, InputEvent e)
            {
                if (!MorePlayersMod.spectatorMode.Value || !e.Valueb || !e.Changed)
                    return true;

                // Handle both keyboard (Esc) and controller (Start/Back) buttons
                if (e.Key != InputEvent.InputKey.Esc && e.Key != InputEvent.InputKey.Start && e.Key != InputEvent.InputKey.Back)
                    return true; // Not a relevant button, continue normally

                // Use PossibleNetWorkNumber directly for spectator detection
                int spectatorNetworkNumber = __instance.PossibleNetWorkNumber;
                if (spectatorNetworkNumber == 0 || !IsSpectator(spectatorNetworkNumber))
                    return true; // Not a spectator, continue normally

                // Find player by network number
                Player player = FindPlayerByNetworkNumber(spectatorNetworkNumber);
                if (player?.PlayerCharacter == null)
                    return true; // No character found, continue normally

                Character character = player.PlayerCharacter;
                
                // Handle spectator exit with Back button in lobby
                if (e.Key == InputEvent.InputKey.Back && 
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "TreeHouseLobby")
                {
                    // Exit spectator mode when B is pressed in lobby
                    Debug.Log($"[SpectatorMod] B button pressed - exiting spectator mode for player {spectatorNetworkNumber}");
                    
                    // Find lobby player and exit spectator mode
                    LobbyPlayer lobbyPlayer = FindLobbyPlayerForCharacter(character);
                    if (lobbyPlayer != null)
                    {
                        SetSpectator(lobbyPlayer.networkNumber, false);
                    }
                    
                    return false; // Prevent further processing
                }
                
                // Handle menu toggle during gameplay (Esc/Start)
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "TreeHouseLobby")
                {
                    // Toggle menu state (cursor already exists)
                    if (!character.InMenu)
                    {
                        character.InMenu = true;
                        character.Freeze(false, false, false);
                        GameEventManager.SendEvent(new PlayerInventoryEvent(true, spectatorNetworkNumber, false));
                        GameEventManager.SendEvent(new SoftPauseEvent(true, spectatorNetworkNumber, NetworkServer.active));
                    }
                    else
                    {
                        character.InMenu = false;
                        character.Unfreeze();
                        GameEventManager.SendEvent(new PlayerInventoryEvent(false, spectatorNetworkNumber, false));
                        GameEventManager.SendEvent(new SoftPauseEvent(false, spectatorNetworkNumber, NetworkServer.active));
                    }
                }

                return false; // Prevent normal menu handling
            }
        }

        // Helper method to find player by network number
        private static Player FindPlayerByNetworkNumber(int networkNumber)
        {
            PlayerManager playerManager = PlayerManager.GetInstance();
            if (playerManager == null)
                return null;

            foreach (Player player in playerManager)
            {
                if (player?.AssociatedLobbyPlayer?.networkNumber == networkNumber)
                    return player;
            }

            return null;
        }

        // Dictionary to track temporary spectator cursors
        private static readonly Dictionary<int, PickCursor> spectatorCursors = new Dictionary<int, PickCursor>();

        // Create a temporary cursor for spectator menu navigation
        private static void CreateSpectatorCursor(Player player, int networkNumber, PickCursor cursorPrefab)
        {
            if (spectatorCursors.ContainsKey(networkNumber))
                return; // Cursor already exists

            try
            {
                // Create cursor at spectator's position
                Vector3 cursorPosition = player.PlayerCharacter != null ? player.PlayerCharacter.transform.position : Vector3.zero;
                PickCursor cursor = UnityEngine.Object.Instantiate(cursorPrefab, cursorPosition, Quaternion.identity);
                
                // Setup cursor properties
                cursor.NetworknetworkNumber = networkNumber;
                cursor.NetworklocalNumber = player.Number;
                cursor.CursorColor = Color.white; // Default color for spectators
                cursor.AssociatedGamePlayer = null; // No GamePlayer for spectators
                
                // Add cursor to inventory book
                VersusControl versusControl = UnityEngine.Object.FindObjectOfType<VersusControl>();
                if (versusControl?.invBookInstance != null)
                {
                    versusControl.invBookInstance.AddPlayer(player.Number, networkNumber, player.UseController, Character.Animals.NONE);
                }
                
                // Enable cursor and make it visible
                cursor.Enable();
                cursor.SetLocalController(player.UseController);
                
                spectatorCursors[networkNumber] = cursor;
                Debug.Log($"[SpectatorMod] Created and enabled persistent cursor for spectator {networkNumber}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SpectatorMod] Error creating spectator cursor: {ex.Message}");
            }
        }

        // Cleanup all spectator cursors
        private static void CleanupAllSpectatorCursors()
        {
            foreach (var kvp in spectatorCursors)
            {
                try
                {
                    PickCursor cursor = kvp.Value;
                    if (cursor != null)
                    {
                        cursor.Disable();
                        UnityEngine.Object.Destroy(cursor.gameObject);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[SpectatorMod] Error cleaning up spectator cursor {kvp.Key}: {ex.Message}");
                }
            }
            
            spectatorCursors.Clear();
            Debug.Log("[SpectatorMod] Cleaned up all spectator cursors");
        }


        // Prevent spectator cursors from picking items (but allow menu interactions)
        [HarmonyPatch(typeof(PickCursor), nameof(PickCursor.OnAccept))]
        static class PickCursorOnAcceptPatch
        {
            static bool Prefix(PickCursor __instance)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return true; // Continue normally if spectator mode is disabled

                // Check if this cursor belongs to a spectator
                if (__instance.networkNumber != 0 && IsSpectator(__instance.networkNumber))
                {
                    // Allow menu interactions but block item picking
                    if (__instance.lastHoveredPick is PickableBlock)
                    {
                        Debug.Log($"[SpectatorMod] Blocked item picking for spectator cursor {__instance.networkNumber}");
                        return false; // Block picking up blocks/items
                    }
                    
                    // Allow button interactions (menu buttons, etc.)
                    if (__instance.lastHoveredPick is PickableButton)
                    {
                        Debug.Log($"[SpectatorMod] Allowed button interaction for spectator cursor {__instance.networkNumber}");
                        return true; // Allow button interactions
                    }
                }

                return true; // Continue normally for non-spectators
            }
        }

        // Prevent spectator cursors from hovering over pickable blocks (but allow buttons)
        [HarmonyPatch(typeof(PickCursor), nameof(PickCursor.checkHoveredPickAdd))]
        static class PickCursorCheckHoveredPickAddPatch
        {
            static bool Prefix(PickCursor __instance, Collider2D c)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return true; // Continue normally if spectator mode is disabled

                // Check if this cursor belongs to a spectator
                if (__instance.networkNumber != 0 && IsSpectator(__instance.networkNumber))
                {
                    // Check what type of pickable this is
                    IPickable pickable = c.GetComponent(typeof(IPickable)) as IPickable;
                    if (pickable == null)
                    {
                        pickable = c.transform.parent.GetComponent(typeof(IPickable)) as IPickable;
                    }
                    
                    // Block hover detection for blocks but allow buttons
                    if (pickable is PickableBlock)
                    {
                        return false; // Block hovering over blocks
                    }
                    
                    // Allow hovering over buttons for menu navigation
                    if (pickable is PickableButton)
                    {
                        return true; // Allow hovering over buttons
                    }
                }

                return true; // Continue normally for non-spectators
            }
        }

        // Prevent spectator cursors from dealing with pickable blocks (but allow buttons)
        [HarmonyPatch(typeof(PickCursor), nameof(PickCursor.dealWithPickable))]
        static class PickCursorDealWithPickablePatch
        {
            static bool Prefix(PickCursor __instance)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return true; // Continue normally if spectator mode is disabled

                // Check if this cursor belongs to a spectator
                if (__instance.networkNumber != 0 && IsSpectator(__instance.networkNumber))
                {
                    // Only block if dealing with pickable blocks, not buttons
                    if (__instance.lastHoveredPick is PickableBlock)
                    {
                        Debug.Log($"[SpectatorMod] Blocked dealWithPickable for spectator cursor {__instance.networkNumber}");
                        return false; // Block the interaction
                    }
                    
                    // Allow button interactions
                    if (__instance.lastHoveredPick is PickableButton)
                    {
                        return true; // Allow button interactions
                    }
                }

                return true; // Continue normally for non-spectators
            }
        }

        // Preserve spectator state when returning to treehouse after a game
        [HarmonyPatch(typeof(LevelSelectController), nameof(LevelSelectController.SetupLobbyAfterWait))]
        static class LevelSelectControllerSetupLobbyAfterWaitPatch
        {
            static void Postfix(LevelSelectController __instance)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return;

                try
                {
                    Debug.Log("[SpectatorMod] SetupLobbyAfterWait - checking for spectator state restoration");
                    
                    // Wait a frame to ensure all player setup is complete
                    __instance.StartCoroutine(RestoreSpectatorStatesDelayed(__instance));
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[SpectatorMod] Error in SetupLobbyAfterWait patch: {ex.Message}");
                }
            }
        }

        // Delayed restoration to ensure all player objects are properly initialized
        private static IEnumerator RestoreSpectatorStatesDelayed(LevelSelectController controller)
        {
            yield return null; // Wait one frame
            
            try
            {
                Debug.Log("[SpectatorMod] Restoring spectator states after returning to treehouse");
                
                // Check if we have any spectators to restore
                bool hasSpectators = false;
                foreach (var kvp in spectatorPlayers)
                {
                    if (kvp.Value) // isSpectator
                    {
                        hasSpectators = true;
                        break;
                    }
                }
                
                if (hasSpectators)
                {
                    Debug.Log("[SpectatorMod] Spectators detected - ensuring couch is enabled");
                    
                    // Ensure UsingHotSeat is enabled for spectators
                    if (!GameState.GetInstance().UsingHotSeat)
                    {
                        GameState.GetInstance().UsingHotSeat = true;
                        Debug.Log("[SpectatorMod] Enabled UsingHotSeat for spectator restoration");
                    }
                    
                    // Lock party mode button if needed
                    if (!controller.PartyModeButton.Locked)
                    {
                        controller.PartyModeButton.Lock();
                        Debug.Log("[SpectatorMod] Locked party mode button for spectator restoration");
                    }
                    
                    // SpectatorHotSeatInstance should now be preserved across lobby transitions
                    // If it's still null here, there might be an issue with the initial creation
                    if (SpectatorHotSeatInstance == null)
                    {
                        Debug.LogWarning("[SpectatorMod] SpectatorHotSeatInstance is null despite preservation - this should not happen");
                    }
                    else
                    {
                        // Reapply couch styling since we're returning to lobby
                        Debug.Log("[SpectatorMod] Reapplying spectator couch styling");
                        SpectatorHotSeatInstance.ApplySpectatorCouchStyling();
                    }
                }
                
                // Iterate through all players and restore spectator state
                foreach (LobbyPlayer lobbyPlayer in LobbyManager.instance.lobbySlots)
                {
                    if (lobbyPlayer != null && IsSpectator(lobbyPlayer.networkNumber))
                    {
                        Debug.Log($"[SpectatorMod] Found spectator {lobbyPlayer.networkNumber}, localNumber: {lobbyPlayer.localNumber}, status: {lobbyPlayer.PlayerStatus}");
                        
                        // Get the player character (use localNumber for PlayerManager lookup)
                        Player player = PlayerManager.GetInstance().GetPlayer(lobbyPlayer.localNumber);
                        Debug.Log($"[SpectatorMod] Player lookup result: {player?.ToString() ?? "null"}");
                        
                        if (player != null)
                        {
                            Debug.Log($"[SpectatorMod] PlayerCharacter: {player.PlayerCharacter?.ToString() ?? "null"}");
                            
                            if (player.PlayerCharacter != null)
                            {
                                Debug.Log($"[SpectatorMod] SpectatorHotSeatInstance: {SpectatorHotSeatInstance?.ToString() ?? "null"}");
                                
                                if (SpectatorHotSeatInstance != null)
                                {
                                    Debug.Log($"[SpectatorMod] Sitting restored spectator {lobbyPlayer.networkNumber} on couch");
                                    
                                    // Update player status to COUCH
                                    lobbyPlayer.PlayerStatus = LobbyPlayer.Status.COUCH;
                                    
                                    // Update the player join indicator
                                    int playerIndex = lobbyPlayer.networkNumber - 1;
                                    if (playerIndex >= 0 && playerIndex < controller.PlayerJoinIndicators.Length)
                                    {
                                        controller.PlayerJoinIndicators[playerIndex].ReadyEnabled();
                                    }
                                    
                                    // Sit the player on the couch
                                    SpectatorHotSeatInstance.SitPlayer(player);
                                    
                                    // Network sync for spectator restoration
                                    if (NetworkServer.active)
                                    {
                                        // Server: broadcast the spectator restoration to all clients
                                        SendSpectatorStatusUpdate(lobbyPlayer.networkNumber, true);
                                        Debug.Log($"[SpectatorMod] Server: Broadcast spectator restoration for player {lobbyPlayer.networkNumber}");
                                    }
                                    else if (NetworkClient.active && lobbyPlayer.IsLocalPlayer)
                                    {
                                        // Client: request server to validate and sync the spectator restoration
                                        RequestSpectatorSitdown(lobbyPlayer.networkNumber);
                                        Debug.Log($"[SpectatorMod] Client: Requested spectator restoration sync for player {lobbyPlayer.networkNumber}");
                                    }
                                    
                                    Debug.Log($"[SpectatorMod] Successfully restored spectator {lobbyPlayer.networkNumber} to couch");
                                }
                                else
                                {
                                    Debug.LogError($"[SpectatorMod] SpectatorHotSeatInstance is null - cannot seat spectator {lobbyPlayer.networkNumber}");
                                }
                            }
                            else
                            {
                                Debug.LogWarning($"[SpectatorMod] PlayerCharacter is null for spectator {lobbyPlayer.networkNumber}");
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"[SpectatorMod] Player is null for spectator {lobbyPlayer.networkNumber}");
                        }
                    }
                }
                
                Debug.Log("[SpectatorMod] Spectator state restoration completed");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[SpectatorMod] Error during delayed spectator state restoration: {ex.Message}");
            }
        }

        // Hook into GameEndEvent to preserve spectator state when game ends
        [HarmonyPatch(typeof(GameEventManager), "SendEvent")]
        static class GameEventManagerSendEventPatch
        {
            static void Prefix(GameEvent.GameEvent e)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return;

                // Check if this is a game end event
                if (e is GameEvent.GameEndEvent gameEndEvent)
                {
                    Debug.Log("[SpectatorMod] GameEndEvent detected - spectator states will be preserved");
                    // Spectator states are already tracked in the static dictionary, so no action needed here
                    // The restoration will happen when returning to treehouse
                }
            }
        }

        // Prevent UsingHotSeat from being disabled when spectators are present
        [HarmonyPatch(typeof(LevelSelectController), "OnLobbyPlayerObjectDestroyed")]
        static class OnLobbyPlayerObjectDestroyedPatch
        {
            static void Postfix(LobbyPlayer lobbyPl)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return;

                try
                {
                    // Check if there are still spectators after a player leaves
                    if (SpectatorHotSeatInstance != null && GameState.GetInstance().UsingHotSeat == false)
                    {
                        bool hasSpectators = false;
                        foreach (var kvp in spectatorPlayers)
                        {
                            if (kvp.Value) // isSpectator
                            {
                                hasSpectators = true;
                                break;
                            }
                        }

                        if (hasSpectators)
                        {
                            Debug.Log("[SpectatorMod] Re-enabling UsingHotSeat because spectators are still present");
                            GameState.GetInstance().UsingHotSeat = true;
                            
                            // Re-lock party mode button if needed
                            var controller = LevelSelectController.lastInstance;
                            if (controller != null && !controller.PartyModeButton.Locked)
                            {
                                controller.PartyModeButton.Lock();
                                Debug.Log("[SpectatorMod] Re-locked party mode button for spectators");
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[SpectatorMod] Error in OnLobbyPlayerObjectDestroyed patch: {ex.Message}");
                }
            }
        }
    }
}
