using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace MorePlayers
{
    /// <summary>
    /// Spectator couch for ONLINE games. The vanilla treehouse couch is the local
    /// hotseat feature (two players, one controller, forces PARTY mode) and is
    /// hidden/inert online. This mod keeps the couch visible online and turns it
    /// into a spectator seat: a player with a picked character presses Accept at
    /// the couch to sit out, spectates started games, and presses Accept again to
    /// rejoin. Local games keep the vanilla hotseat couch untouched.
    ///
    /// Sync model (mirrors the game's own relay pattern, see
    /// notes/UCH_NETWORKING_ANALYSIS.md):
    ///  - requests go to the server (msg 1010), the server validates and
    ///    broadcasts the resulting state (msg 1011) via NetworkServer.SendToAll,
    ///    which includes the host's own local client;
    ///  - every peer applies the same state change from the broadcast; the
    ///    requester never applies ahead of it;
    ///  - all bookkeeping is keyed by LobbyPlayer.networkNumber and resolved via
    ///    LobbyManager.GetLobbyPlayer / CharacterInstance, which exist on every
    ///    peer (PlayerManager/Player only exist for local players);
    ///  - PlayerStatus (COUCH/CHARACTER) is only written on the owning peer —
    ///    writes on non-owned LobbyPlayers are silent no-ops;
    ///  - seat visuals are enforced by an idempotent per-frame reconciler in the
    ///    treehouse (hooked off HotSeat.Update), so late joiners and lobby
    ///    reloads self-heal from the synced dictionary. Characters are only
    ///    teleported on their owning peer; position replicates from there.
    /// </summary>
    [HarmonyPatch]
    static class SpectatorCouch
    {
        // Temporary allocation. Keep clear of late-join's 1001-1006 range.
        // See glorpy_knowledge/network-message-id-coordination-todo.md.
        private const short RequestMsgType = 1010; // client -> server
        private const short StateMsgType = 1011;   // server -> all clients
        private const int OnlineSpectatorSeatCount = 8;

        // networkNumber -> is spectator. The single source of truth on each peer,
        // written only by ApplyState (from server broadcasts) and cleanup paths.
        private static readonly Dictionary<int, bool> spectators = new Dictionary<int, bool>();
        private static bool configurationBoundaryInitialized;

        public static void InitializeConfigurationBoundary()
        {
            if (configurationBoundaryInitialized || MorePlayersMod.spectatorMode == null)
                return;

            configurationBoundaryInitialized = true;
            MorePlayersMod.spectatorMode.SettingChanged += OnSpectatorModeSettingChanged;
        }

        private static void OnSpectatorModeSettingChanged(object sender, System.EventArgs args)
        {
            if (!MorePlayersMod.spectatorMode.Value)
                ClearSpectatorState("spectator mode disabled");
        }

        public static bool IsSpectator(int networkNumber)
        {
            return spectators.TryGetValue(networkNumber, out bool spec) && spec;
        }

        public static int SpectatorCount
        {
            get
            {
                int count = 0;
                foreach (var kvp in spectators)
                {
                    if (kvp.Value) count++;
                }
                return count;
            }
        }

        // Only ever active in online games; local play keeps the vanilla couch.
        private static bool Active
        {
            get
            {
                return MorePlayersMod.spectatorMode.Value
                    && LobbyManager.instance != null
                    && LobbyManager.instance.IsInOnlineGame;
            }
        }

        public class SpectatorMessage : MessageBase
        {
            public int networkNumber;
            public bool isSpectator;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(networkNumber);
                writer.Write(isSpectator);
            }

            public override void Deserialize(NetworkReader reader)
            {
                networkNumber = reader.ReadInt32();
                isSpectator = reader.ReadBoolean();
            }
        }

        // ------------------------------------------------------------------
        // Requests and state application
        // ------------------------------------------------------------------

        public static void RequestSetSpectator(int networkNumber, bool isSpectator)
        {
            if (!Active)
            {
                Debug.LogWarning($"[SpectatorMod] ignored spectator request outside active online lobby "
                    + $"net={networkNumber} spec={isSpectator}");
                return;
            }

            if (NetworkServer.active)
            {
                ServerDecide(networkNumber, isSpectator);
            }
            else if (LobbyManager.instance != null && LobbyManager.instance.client != null)
            {
                var msg = new SpectatorMessage { networkNumber = networkNumber, isSpectator = isSpectator };
                LobbyManager.instance.client.Send(RequestMsgType, msg);
                Debug.Log($"[SpectatorMod] sent request net={networkNumber} spec={isSpectator}");
            }
        }

        // Server-side: validate a sit/unsit request and broadcast the result to
        // every peer (SendToAll reaches the host's local client too, so the host
        // applies through the same path as everyone else).
        private static void ServerDecide(int networkNumber, bool isSpectator)
        {
            LobbyPlayer lobbyPlayer = LobbyManager.instance?.GetLobbyPlayer(networkNumber);
            if (lobbyPlayer == null)
            {
                // Unsit for an already-gone player is still broadcast so stale
                // entries clear everywhere (e.g. disconnect cleanup).
                if (isSpectator)
                {
                    Debug.LogWarning($"[SpectatorMod] server denied sit: no lobby player {networkNumber}");
                    return;
                }
            }

            if (isSpectator)
            {
                if (IsSpectator(networkNumber))
                    return; // already seated, idempotent
                if (lobbyPlayer.PlayerStatus != LobbyPlayer.Status.CHARACTER)
                {
                    Debug.LogWarning($"[SpectatorMod] server denied sit for {networkNumber}: status={lobbyPlayer.PlayerStatus}");
                    return;
                }
                HotSeat couch = LevelSelectController.lastInstance != null
                    ? LevelSelectController.lastInstance.HotSeatCouch
                    : null;
                if (couch == null)
                {
                    couch = Object.FindObjectOfType<HotSeat>();
                }
                EnsureExpandedSeats(couch);
                int seatCount = couch != null && couch.seats != null
                    ? couch.seats.Length
                    : 0;
                if (seatCount == 0 || SpectatorCount >= seatCount)
                {
                    Debug.LogWarning($"[SpectatorMod] server denied sit for {networkNumber}: no free seat "
                        + $"(lastInstance={(LevelSelectController.lastInstance != null)}, couch={(couch != null)}, "
                        + $"seats={seatCount}, spectators={SpectatorCount})");
                    return;
                }
            }
            else if (!IsSpectator(networkNumber))
            {
                return; // not a spectator, nothing to do
            }

            var msg = new SpectatorMessage { networkNumber = networkNumber, isSpectator = isSpectator };
            NetworkServer.SendToAll(StateMsgType, msg);
        }

        private static void OnServerRequest(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<SpectatorMessage>();
            Debug.Log($"[SpectatorMod] server got request net={msg.networkNumber} spec={msg.isSpectator}");
            ServerDecide(msg.networkNumber, msg.isSpectator);
        }

        private static void OnClientState(NetworkMessage netMsg)
        {
            var msg = netMsg.ReadMessage<SpectatorMessage>();
            ApplyState(msg.networkNumber, msg.isSpectator);
        }

        // Runs identically on every peer. Updates the dictionary, transitions the
        // lobby status on the owning peer, and lets the reconciler do the seat
        // visuals on its next tick.
        private static void ApplyState(int networkNumber, bool isSpectator)
        {
            if (!Active)
            {
                Debug.LogWarning($"[SpectatorMod] ignored spectator state outside active online lobby "
                    + $"net={networkNumber} spec={isSpectator}");
                return;
            }

            spectators[networkNumber] = isSpectator;
            Debug.Log($"[SpectatorMod] STATE net={networkNumber} spec={isSpectator} "
                + $"peer={(NetworkServer.active ? "host" : "client")}");

            LobbyPlayer lobbyPlayer = LobbyManager.instance?.GetLobbyPlayer(networkNumber);
            if (lobbyPlayer != null && lobbyPlayer.IsLocalPlayer)
            {
                if (isSpectator && lobbyPlayer.PlayerStatus == LobbyPlayer.Status.CHARACTER)
                {
                    lobbyPlayer.PlayerStatus = LobbyPlayer.Status.COUCH;
                }
                else if (!isSpectator && lobbyPlayer.PlayerStatus == LobbyPlayer.Status.COUCH)
                {
                    lobbyPlayer.PlayerStatus = LobbyPlayer.Status.CHARACTER;
                }
            }
        }

        // ------------------------------------------------------------------
        // Network plumbing patches
        // ------------------------------------------------------------------

        [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.OnStartClient))]
        static class RegisterHandlersPatch
        {
            static void Postfix(NetworkClient lobbyClient)
            {
                if (!MorePlayersMod.spectatorMode.Value)
                    return;

                if (NetworkServer.active)
                {
                    NetworkServer.RegisterHandler(RequestMsgType, OnServerRequest);
                }
                if (lobbyClient != null)
                {
                    lobbyClient.RegisterHandler(StateMsgType, OnClientState);
                }
                Debug.Log("[SpectatorMod] network handlers registered "
                    + $"(server={NetworkServer.active})");
            }
        }

        // Bring late joiners up to date with the current spectator set.
        [HarmonyPatch(typeof(LobbyManager), "OnLobbyServerConnect")]
        static class LateJoinSyncPatch
        {
            static void Postfix(NetworkConnection conn)
            {
                if (!MorePlayersMod.spectatorMode.Value || !NetworkServer.active)
                    return;

                foreach (var kvp in spectators)
                {
                    if (kvp.Value)
                    {
                        conn.Send(StateMsgType, new SpectatorMessage
                        {
                            networkNumber = kvp.Key,
                            isSpectator = true
                        });
                    }
                }
            }
        }

        // A leaving lobby player must not leave a stale spectator entry behind —
        // network numbers get reused by later joiners.
        [HarmonyPatch(typeof(LevelSelectController), "OnLobbyPlayerObjectDestroyed")]
        static class PlayerLeftCleanupPatch
        {
            static void Postfix(LobbyPlayer lobbyPl)
            {
                if (!MorePlayersMod.spectatorMode.Value || lobbyPl == null)
                    return;

                if (NetworkServer.active && IsSpectator(lobbyPl.networkNumber))
                {
                    Debug.Log($"[SpectatorMod] spectator {lobbyPl.networkNumber} left, clearing");
                    ServerDecide(lobbyPl.networkNumber, false);
                }
            }
        }

        [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.OnDestroy))]
        static class LobbyTeardownPatch
        {
            static void Prefix()
            {
                // LobbyManager is a scene object and is destroyed during the
                // normal level -> treehouse transition while the UNet session
                // remains alive.  Clearing here used to erase the spectator
                // set just before the new treehouse couch could reconcile it.
                if (!NetworkServer.active && !NetworkClient.active)
                {
                    ClearSpectatorState("lobby destroyed after network shutdown");
                }
                else if (spectators.Count > 0)
                {
                    Debug.Log("[SpectatorMod] lobby scene object destroyed while connected; "
                        + $"preserving {SpectatorCount} spectator(s)");
                }
            }
        }

        // Explicit session teardown paths. Unlike OnDestroy, these indicate
        // that the peer really is leaving the network session, so retained
        // network numbers must not leak into a later lobby.
        [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.Disconnect))]
        static class DisconnectCleanupPatch
        {
            static void Prefix()
            {
                ClearSpectatorState("disconnect");
            }
        }

        [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.OnStopClient))]
        static class StopClientCleanupPatch
        {
            static void Prefix()
            {
                ClearSpectatorState("client stopped");
            }
        }

        [HarmonyPatch(typeof(NetworkLobbyManager), nameof(NetworkLobbyManager.OnStopHost))]
        static class StopHostCleanupPatch
        {
            static void Prefix()
            {
                ClearSpectatorState("host stopped");
            }
        }

        private static void ClearSpectatorState(string reason)
        {
            if (spectators.Count == 0)
                return;

            Debug.Log($"[SpectatorMod] {reason}, clearing {SpectatorCount} spectator(s)");
            spectators.Clear();
        }

        // ------------------------------------------------------------------
        // Couch visibility + seat reconciliation (treehouse only)
        // ------------------------------------------------------------------

        // Vanilla creates three seats from serialized transforms in Start().
        // Keep those scene transforms untouched (offline hotseat still uses the
        // vanilla layout), but replace the online runtime array with eight
        // evenly squeezed positions spanning the original left/right bounds.
        [HarmonyPatch(typeof(HotSeat), "Start")]
        static class ExpandOnlineCouchPatch
        {
            static void Postfix(HotSeat __instance)
            {
                if (Active)
                    EnsureExpandedSeats(__instance);
            }
        }

        private static void EnsureExpandedSeats(HotSeat couch)
        {
            if (couch == null || !Active || couch.seats == null
                || couch.seats.Length >= OnlineSpectatorSeatCount)
                return;

            Vector3 left;
            Vector3 right;
            if (couch.SeatPositions != null && couch.SeatPositions.Length > 0)
            {
                left = couch.SeatPositions[0].position;
                right = left;
                foreach (Transform position in couch.SeatPositions)
                {
                    if (position == null)
                        continue;
                    if (position.position.x < left.x)
                        left = position.position;
                    if (position.position.x > right.x)
                        right = position.position;
                }
            }
            else
            {
                left = couch.transform.position;
                right = left;
            }

            HotSeat.Seat[] oldSeats = couch.seats;
            HotSeat.Seat[] expanded = new HotSeat.Seat[OnlineSpectatorSeatCount];
            for (int i = 0; i < expanded.Length; i++)
            {
                float amount = expanded.Length == 1 ? 0.5f : (float)i / (expanded.Length - 1);
                expanded[i] = new HotSeat.Seat(Vector3.Lerp(left, right, amount));
            }

            // Normally expansion runs before anyone can sit. Preserve any
            // already-occupied characters if a late initialization path calls
            // this after requests have begun.
            int occupiedIndex = 0;
            foreach (HotSeat.Seat oldSeat in oldSeats)
            {
                if (oldSeat == null || !oldSeat.occupied || oldSeat.character == null)
                    continue;
                expanded[occupiedIndex].occupied = true;
                expanded[occupiedIndex].character = oldSeat.character;
                occupiedIndex++;
            }

            couch.seats = expanded;
            Debug.Log($"[SpectatorMod] expanded online couch from {oldSeats.Length} "
                + $"to {expanded.Length} seats");
        }

        // Vanilla HotSeat tracks only Character objects in charactersAtCouch,
        // even though a Character has many child colliders. The first child
        // collider to leave removes the Character from that list while other
        // colliders may still overlap the couch. This is especially easy to
        // trigger while jumping. Determine eligibility from the live collider
        // contacts instead so one child exiting cannot invalidate the others.
        [HarmonyPatch(typeof(HotSeat), nameof(HotSeat.CharacterAtCouch))]
        static class HotSeatCharacterAtCouchPatch
        {
            static bool Prefix(HotSeat __instance, Character c, ref bool __result)
            {
                if (!Active)
                    return true;

                __result = false;
                if (c == null || !c.gameObject.activeInHierarchy)
                    return false;

                Collider2D[] couchColliders = __instance.GetComponents<Collider2D>();
                Collider2D[] characterColliders = c.GetComponentsInChildren<Collider2D>();
                foreach (Collider2D couchCollider in couchColliders)
                {
                    if (couchCollider == null || !couchCollider.isActiveAndEnabled)
                        continue;

                    foreach (Collider2D characterCollider in characterColliders)
                    {
                        if (characterCollider != null && characterCollider.isActiveAndEnabled
                            && couchCollider.IsTouching(characterCollider))
                        {
                            __result = true;
                            return false;
                        }
                    }
                }
                return false;
            }
        }

        // Spectators occupy the couch visually, but they are not vanilla
        // shared-controller hotseat players. LevelSelectController subtracts
        // GetSeatsTaken() from its ready-player target while still counting
        // COUCH-status lobby players as present. If every online player is a
        // spectator that makes the target zero and starts the previous mode.
        [HarmonyPatch(typeof(HotSeat), nameof(HotSeat.GetSeatsTaken))]
        static class HotSeatGetSeatsTakenPatch
        {
            static bool Prefix(ref int __result)
            {
                if (!Active)
                    return true;

                __result = 0;
                return false;
            }
        }

        // Vanilla Update() hides the couch in online games and SitPlayer no-ops
        // while hidden. Online with spectator mode we keep it shown and reconcile
        // the seats against the synced spectator set every frame.
        [HarmonyPatch(typeof(HotSeat), "Update")]
        static class HotSeatUpdatePatch
        {
            static bool Prefix(HotSeat __instance)
            {
                if (!Active)
                    return true; // vanilla behavior (local couch, or mode off)

                EnsureExpandedSeats(__instance);
                __instance.show();
                ApplyCouchStyling(__instance);
                ReconcileSeats(__instance);
                AutoSpectate.Tick(__instance);
                return false;
            }
        }

        // Idempotent: seats every spectator character, frees seats of
        // non-spectators and departed characters. Characters are teleported only
        // on their owning peer; remote peers just mirror pose and bookkeeping.
        private static void ReconcileSeats(HotSeat couch)
        {
            if (couch.seats == null || LobbyManager.instance == null)
                return;

            foreach (NetworkLobbyPlayer slot in LobbyManager.instance.lobbySlots)
            {
                LobbyPlayer lobbyPlayer = slot as LobbyPlayer;
                if (lobbyPlayer == null || lobbyPlayer.CharacterInstance == null)
                    continue;

                if (IsSpectator(lobbyPlayer.networkNumber))
                {
                    SeatCharacter(couch, lobbyPlayer.CharacterInstance, lobbyPlayer.IsLocalPlayer,
                        lobbyPlayer.networkNumber);
                }
                else
                {
                    UnseatCharacter(couch, lobbyPlayer.CharacterInstance);
                }
            }

            // Free seats whose character no longer exists (left/despawned).
            foreach (HotSeat.Seat seat in couch.seats)
            {
                if (seat.occupied && seat.character == null)
                {
                    seat.occupied = false;
                }
            }
        }

        private static void SeatCharacter(HotSeat couch, Character character, bool isOwner, int networkNumber)
        {
            foreach (HotSeat.Seat seat in couch.seats)
            {
                if (seat.occupied && seat.character == character)
                    return; // already seated
            }

            foreach (HotSeat.Seat seat in couch.seats)
            {
                if (seat.occupied)
                    continue;

                seat.occupied = true;
                seat.character = character;
                character.Sitting = true;
                if (isOwner)
                {
                    // Positions are owner-authoritative; remote peers get this
                    // via normal character sync.
                    character.transform.position = seat.position;
                    var body = character.GetComponent<Rigidbody2D>();
                    if (body != null) body.velocity = Vector2.zero;
                    character.Ready = true;
                }
                foreach (SpriteRenderer renderer in character.GetComponentsInChildren<SpriteRenderer>())
                {
                    renderer.sortingLayerName = "Default2";
                }
                Debug.Log($"[SpectatorMod] seated net={networkNumber} owner={isOwner}");
                return;
            }
        }

        private static void UnseatCharacter(HotSeat couch, Character character)
        {
            foreach (HotSeat.Seat seat in couch.seats)
            {
                if (!seat.occupied || seat.character != character)
                    continue;

                seat.occupied = false;
                seat.character = null;
                character.Sitting = false;
                character.Ready = false;
                foreach (SpriteRenderer renderer in character.GetComponentsInChildren<SpriteRenderer>())
                {
                    renderer.sortingLayerName = "Player";
                }
                Debug.Log("[SpectatorMod] unseated character " + character.name);
                return;
            }
        }

        // Green tint + "Spectator Couch" label so the online couch is clearly not
        // the vanilla hotseat. Applied once per HotSeat instance.
        private static int styledCouchId;

        private static void ApplyCouchStyling(HotSeat couch)
        {
            if (couch.GetInstanceID() == styledCouchId)
                return;
            styledCouchId = couch.GetInstanceID();

            foreach (Text text in couch.GetComponentsInChildren<Text>())
            {
                string lower = text.text != null ? text.text.ToLowerInvariant() : "";
                if (lower.Contains("couch") || lower.Contains("hot"))
                {
                    text.text = "Spectator Couch";
                }
            }
            foreach (SpriteRenderer renderer in couch.GetComponentsInChildren<SpriteRenderer>())
            {
                renderer.color = new Color(0.55f, 1f, 0.55f, 1f);
            }
        }

        // ------------------------------------------------------------------
        // Entry / exit input (treehouse)
        // ------------------------------------------------------------------

        // Runs before the vanilla handler. Mirrors the vanilla local-player
        // resolution, then: Accept at the couch with a picked character requests
        // sitting (and blocks the dead-online vanilla couch branch); Accept while
        // seated as spectator requests standing up.
        [HarmonyPatch(typeof(LevelSelectController), nameof(LevelSelectController.ReceiveEvent))]
        static class CouchInputPatch
        {
            static bool Prefix(LevelSelectController __instance, InputEvent e)
            {
                if (!Active)
                    return true;
                if (__instance.levelChosen || __instance.castingVotes || Controller.FullScreenComputerIsActive)
                    return true;
                if (e.Key != InputEvent.InputKey.Accept || !e.Valueb || !e.Changed)
                    return true;
                if (e.Sender.IsKeyboard && Controller.InputFieldWasActiveRecently)
                    return true;
                if (e.Sender.GetControlMask() <= 0)
                    return true;

                foreach (LobbyPlayer lobbyPlayer in __instance.JoinedPlayers)
                {
                    if (lobbyPlayer == null || !lobbyPlayer.IsLocalPlayer
                        || !e.Sender.ControlsPlayer(lobbyPlayer.localNumber))
                        continue;

                    Character character = lobbyPlayer.CharacterInstance;

                    // Stand up: seated spectator pressed Accept.
                    if (lobbyPlayer.PlayerStatus == LobbyPlayer.Status.COUCH
                        && IsSpectator(lobbyPlayer.networkNumber))
                    {
                        Debug.Log($"[SpectatorMod] local exit request net={lobbyPlayer.networkNumber}");
                        RequestSetSpectator(lobbyPlayer.networkNumber, false);
                        return false;
                    }

                    // Sit down: character standing at the couch pressed Accept.
                    if (lobbyPlayer.PlayerStatus == LobbyPlayer.Status.CHARACTER
                        && character != null && !character.InMenu
                        && __instance.HotSeatCouch != null
                        && __instance.HotSeatCouch.CharacterAtCouch(character))
                    {
                        Debug.Log($"[SpectatorMod] local sit request net={lobbyPlayer.networkNumber}");
                        RequestSetSpectator(lobbyPlayer.networkNumber, true);
                        return false;
                    }
                }
                return true;
            }
        }

        // Suicide/unpick while seated would drop the spectator's character and
        // spawn a lobby cursor mid-couch; swallow it.
        [HarmonyPatch(typeof(LobbyPlayer), nameof(LobbyPlayer.ReceiveEvent))]
        static class BlockSuicideWhileSeatedPatch
        {
            static bool Prefix(LobbyPlayer __instance, InputEvent e)
            {
                if (!Active)
                    return true;
                if (e.Key == InputEvent.InputKey.Suicide && IsSpectator(__instance.networkNumber))
                {
                    return false;
                }
                return true;
            }
        }

        // ------------------------------------------------------------------
        // Game start / return to lobby
        // ------------------------------------------------------------------

        // Every peer removes spectators from the play queue the same way, so all
        // downstream counts (scoreboard, turn order, inventory book) agree.
        // Their spawned character/cursor objects are parked inactive.
        [HarmonyPatch(typeof(GameControl), "SetupStart")]
        static class FilterSpectatorsFromGamePatch
        {
            static void Postfix(GameControl __instance)
            {
                if (!Active || SpectatorCount == 0)
                    return;

                var filtered = new Queue<GamePlayer>();
                int removed = 0;
                foreach (GamePlayer gamePlayer in __instance.PlayerQueue)
                {
                    if (gamePlayer != null && IsSpectator(gamePlayer.networkNumber))
                    {
                        removed++;
                        if (gamePlayer.CharacterInstance != null)
                            gamePlayer.CharacterInstance.gameObject.SetActive(false);
                        if (gamePlayer.CursorInstance != null)
                            gamePlayer.CursorInstance.gameObject.SetActive(false);
                    }
                    else
                    {
                        filtered.Enqueue(gamePlayer);
                    }
                }

                if (removed > 0)
                {
                    __instance.PlayerQueue = filtered;
                    Debug.Log($"[SpectatorMod] filtered {removed} spectator(s) from PlayerQueue, "
                        + $"{filtered.Count} players remain");
                }
            }
        }

        // Returning to the treehouse, vanilla restores COUCH-status players as
        // hotseat players (forces UsingHotSeat + locks the party-mode button).
        // For online spectators keep the seat but undo those two side effects.
        [HarmonyPatch(typeof(LevelSelectController), "setupController")]
        static class RestoreSpectatorNotHotseatPatch
        {
            static void Postfix(LevelSelectController __instance, LobbyPlayer lobbyPl)
            {
                if (!Active || lobbyPl == null || !IsSpectator(lobbyPl.networkNumber))
                    return;

                // Scene return recreates lobby characters and resets their
                // owning LobbyPlayer status to CHARACTER. Restore COUCH on the
                // owner so Accept means "stand up" again. Non-owner writes to
                // PlayerStatus are intentionally avoided.
                if (lobbyPl.IsLocalPlayer && lobbyPl.PlayerStatus != LobbyPlayer.Status.COUCH)
                {
                    lobbyPl.PlayerStatus = LobbyPlayer.Status.COUCH;
                }
                GameState.GetInstance().UsingHotSeat = false;
                if (__instance.PartyModeButton != null && __instance.PartyModeButton.Locked)
                {
                    __instance.PartyModeButton.Unlock();
                }
                Debug.Log($"[SpectatorMod] restored spectator {lobbyPl.networkNumber} without hotseat side effects");
            }
        }

        // ------------------------------------------------------------------
        // Test hook: --auto-spectate[=SECONDS]
        // ------------------------------------------------------------------

        // Hands-free E2E testing: once the local player has a character in the
        // treehouse, walk them onto the couch and request sitting. Driven by the
        // per-frame reconciler tick above; no dependency on any test harness mod.
        private static class AutoSpectate
        {
            private const string Argument = "--auto-spectate";

            private static readonly bool requested;
            private static readonly float delaySeconds = 2f;
            private static bool done;
            private static float readySince = -1f;
            private static float nextPickAttempt;

            static AutoSpectate()
            {
                foreach (string arg in System.Environment.GetCommandLineArgs())
                {
                    if (!arg.StartsWith(Argument, System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    requested = true;
                    string value = arg.Substring(Argument.Length).TrimStart('=').Trim();
                    if (!string.IsNullOrEmpty(value)
                        && float.TryParse(value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                    {
                        delaySeconds = parsed;
                    }
                    Debug.Log($"[SpectatorMod] auto-spectate armed (delay {delaySeconds}s)");
                    break;
                }
            }

            public static void Tick(HotSeat couch)
            {
                if (!requested || done || LobbyManager.instance == null)
                    return;

                foreach (NetworkLobbyPlayer slot in LobbyManager.instance.lobbySlots)
                {
                    LobbyPlayer lobbyPlayer = slot as LobbyPlayer;
                    if (lobbyPlayer == null || !lobbyPlayer.IsLocalPlayer)
                        continue;

                    // Still on the lobby cursor: pick any free character first
                    // (sitting requires CHARACTER status). Retried until it lands.
                    if (lobbyPlayer.PlayerStatus == LobbyPlayer.Status.CURSOR
                        && Time.time >= nextPickAttempt)
                    {
                        nextPickAttempt = Time.time + 3f;
                        foreach (Character candidate in Object.FindObjectsOfType<Character>())
                        {
                            if (!candidate.Picked && candidate.gameObject.activeInHierarchy)
                            {
                                Debug.Log("[SpectatorMod] auto-spectate: picking " + candidate.CharacterSprite);
                                lobbyPlayer.RequestPickCharacter(candidate);
                                break;
                            }
                        }
                        continue;
                    }

                    if (lobbyPlayer.PlayerStatus != LobbyPlayer.Status.CHARACTER
                        || lobbyPlayer.CharacterInstance == null)
                        continue;

                    if (readySince < 0f)
                    {
                        readySince = Time.time;
                        return;
                    }
                    if (Time.time - readySince < delaySeconds)
                        return;

                    Debug.Log($"[SpectatorMod] auto-spectate: seating net={lobbyPlayer.networkNumber}");
                    lobbyPlayer.CharacterInstance.transform.position = couch.transform.position;
                    RequestSetSpectator(lobbyPlayer.networkNumber, true);
                    done = true;
                    return;
                }
            }
        }
    }
}
