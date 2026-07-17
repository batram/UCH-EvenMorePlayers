using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;

namespace MorePlayers
{
    // Custom spectator hotseat component that replaces original hotseat logic
    public class SpectatorHotSeat : MonoBehaviour
    {
        public List<Character> charactersAtCouch = new List<Character>();
        public Transform[] SeatPositions;
        public HotSeat.Seat[] seats;
        protected Dictionary<Controller, Player[]> playerControlMap = new Dictionary<Controller, Player[]>();
        
        private void Awake()
        {
            // Copy seat positions from original component if it exists
            HotSeat originalHotSeat = GetComponent<HotSeat>();
            if (originalHotSeat != null)
            {
                SeatPositions = originalHotSeat.SeatPositions;
            }
            
            // Initialize player control map with keyboard
            GameState instance = GameState.GetInstance();
            if (instance != null && instance.Keyboard != null)
            {
                playerControlMap.Add(instance.Keyboard, new Player[4]);
            }
            
            // Initialize seats
            seats = new HotSeat.Seat[SeatPositions != null ? SeatPositions.Length : 4];
            for (int i = 0; i < seats.Length; i++)
            {
                if (SeatPositions != null && i < SeatPositions.Length)
                {
                    seats[i] = new HotSeat.Seat(SeatPositions[i].position);
                }
                else
                {
                    seats[i] = new HotSeat.Seat(Vector3.zero);
                }
            }
        }
        
        private void OnTriggerEnter2D(Collider2D c)
        {
            Character character = c.gameObject.GetComponentInParent<Character>();
            if (character != null && !charactersAtCouch.Contains(character))
            {
                // Check if this character belongs to a spectator
                LobbyPlayer lobbyPlayer = FindLobbyPlayerForCharacter(character);
                if (lobbyPlayer != null && SpectatorModPatches.IsSpectator(lobbyPlayer.networkNumber))
                {
                    Debug.Log($"[SpectatorHotSeat] Blocked hotseat collision for spectator {lobbyPlayer.networkNumber}");
                    return; // Prevent collision for spectators
                }
                
                charactersAtCouch.Add(character);
                Debug.Log($"[SpectatorHotSeat] Character {character.name} entered hotseat zone");
            }
        }
        
        private void OnTriggerExit2D(Collider2D c)
        {
            Character character = c.gameObject.GetComponentInParent<Character>();
            if (character != null && charactersAtCouch.Contains(character))
            {
                charactersAtCouch.Remove(character);
                Debug.Log($"[SpectatorHotSeat] Character {character.name} exited hotseat zone");
            }
        }
        
        // Helper method to find lobby player for character
        private LobbyPlayer FindLobbyPlayerForCharacter(Character character)
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
        
        // Public methods that mirror HotSeat interface
        public void SitPlayer(Player player)
        {
            Debug.Log($"[SpectatorHotSeat] start sit player {player.Number} on couch");

            if (player == null || player.PlayerCharacter == null) return;
            
            // Check if player is already seated
            foreach (HotSeat.Seat seat in seats)
            {
                if (seat.character == player.PlayerCharacter)
                {
                    return;
                }
            }
            
            foreach (HotSeat.Seat seat in seats)
            {
                if (!seat.occupied)
                {
                    seat.occupied = true;
                    seat.character = player.PlayerCharacter;
                    seat.character.transform.position = seat.position;
                    seat.character.GetComponent<Rigidbody2D>().velocity = Vector2.zero;
                    seat.character.Ready = true;
                    seat.character.Sitting = true;
                    
                    // Update sprite layers
                    SpriteRenderer[] renderers = seat.character.GetComponentsInChildren<SpriteRenderer>();
                    foreach (SpriteRenderer renderer in renderers)
                    {
                        renderer.sortingLayerName = "Default2";
                    }
                    
                    // Handle controller mapping
                    if (playerControlMap.ContainsKey(player.UseController))
                    {
                        Player[] controllerPlayers = playerControlMap[player.UseController];
                        for (int i = 0; i < 4; i++)
                        {
                            if (controllerPlayers[i] == null)
                            {
                                controllerPlayers[i] = player;
                                break;
                            }
                        }
                    }
                    else
                    {
                        playerControlMap.Add(player.UseController, new Player[4]);
                        playerControlMap[player.UseController][0] = player;
                    }
                    
                    Debug.Log($"[SpectatorHotSeat] Seated player {player.Number} on couch");
                    return;
                }
            }
        }
        
        public void UnsitPlayer(Player player)
        {
            if (player == null || player.PlayerCharacter == null) return;
            
            foreach (HotSeat.Seat seat in seats)
            {
                if (seat.occupied && seat.character == player.PlayerCharacter)
                {
                    seat.occupied = false;
                    seat.character.Ready = false;
                    seat.character.Sitting = false;
                    
                    // Restore sprite layers
                    SpriteRenderer[] renderers = seat.character.GetComponentsInChildren<SpriteRenderer>();
                    foreach (SpriteRenderer renderer in renderers)
                    {
                        renderer.sortingLayerName = "Player";
                    }
                    
                    // Remove from controller mapping
                    if (playerControlMap.ContainsKey(player.UseController))
                    {
                        Player[] controllerPlayers = playerControlMap[player.UseController];
                        for (int i = 0; i < 4; i++)
                        {
                            if (controllerPlayers[i] == player)
                            {
                                controllerPlayers[i] = null;
                                break;
                            }
                        }
                    }
                    
                    seat.character = null;
                    Debug.Log($"[SpectatorHotSeat] Unseated player {player.Number} from couch");
                    return;
                }
            }
        }
        
        public bool IsSeatAvailable()
        {
            foreach (HotSeat.Seat seat in seats)
            {
                if (!seat.occupied)
                {
                    return true;
                }
            }
            return false;
        }
        
        public int GetSeatsTaken()
        {
            int count = 0;
            foreach (HotSeat.Seat seat in seats)
            {
                if (seat.occupied)
                {
                    count++;
                }
            }
            return count;
        }
        
        public bool CharacterAtCouch(Character c)
        {
            return charactersAtCouch.Contains(c);
        }
        
        public bool PlayerSitting(Player player)
        {
            if (player == null || player.UseController == null) return false;
            
            if (!playerControlMap.ContainsKey(player.UseController))
            {
                return false;
            }
            Player[] controllerPlayers = playerControlMap[player.UseController];
            for (int i = 0; i < 4; i++)
            {
                if (controllerPlayers[i] == player)
                {
                    return true;
                }
            }
            return false;
        }
        
        // Apply spectator couch styling (text and color)
        public void ApplySpectatorCouchStyling()
        {
            // Find Text components and update them
            Text[] textComponents = GetComponentsInChildren<Text>();
            foreach (Text text in textComponents)
            {
                if (text.text != null && (text.text.ToLower().Contains("couch") || text.text.ToLower().Contains("hot")))
                {
                    text.text = "Spectator Couch";
                    Debug.Log("[SpectatorHotSeat] Changed couch text to 'Spectator Couch'");
                }
            }
            
            // Change couch color to green
            ChangeCouchColor();
        }
        
        private void ChangeCouchColor()
        {
            // Find all Renderer components in GameObject and its children
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                if (renderer.material != null)
                {
                    // Change material color to green with some transparency
                    Color greenColor = new Color(0f, 1f, 0f, 0.8f); // Green with 80% opacity
                    renderer.material.color = greenColor;
                    Debug.Log($"[SpectatorHotSeat] Changed {renderer.gameObject.name} color to green");
                }
            }
            
            // Also change SpriteRenderer colors
            SpriteRenderer[] spriteRenderers = GetComponentsInChildren<SpriteRenderer>();
            foreach (SpriteRenderer spriteRenderer in spriteRenderers)
            {
                if (spriteRenderer.color != null)
                {
                    // Change sprite color to green
                    Color greenColor = new Color(0f, 1f, 0f, 1f); // Full green
                    spriteRenderer.color = greenColor;
                    Debug.Log($"[SpectatorHotSeat] Changed sprite {spriteRenderer.gameObject.name} color to green");
                }
            }
        }

        // Inner class to match HotSeat.Seat structure
        public class Seat
        {
            public Vector3 position;
            public bool occupied;
            public Character character;
            
            public Seat(Vector3 pos)
            {
                position = pos;
                occupied = false;
                character = null;
            }
        }
    }
}
