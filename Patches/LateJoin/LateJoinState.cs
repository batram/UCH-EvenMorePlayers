using System.Collections.Generic;
using UnityEngine.Networking;

namespace MorePlayers.LateJoin
{
    // Shared runtime state for the late-join feature. All members are reset on
    // disconnect/lobby teardown via Reset().
    public static class LateJoinState
    {
        // Client side: set before/while connecting when we know the session we are
        // joining is already past the treehouse (match progress != 0 or the
        // MP_lateJoinScene lobby data key is set).
        public static bool ClientJoiningLate;

        // Client side: true once the late joiner has been activated (character
        // spawned) or handed to the spectator system; ends the "defer everything"
        // behavior of the connection patches.
        public static bool ClientIntegrated;

        // Host side: connections accepted while a level scene was active, still
        // waiting for their LobbyPlayer to appear. Exempt from
        // DisconnectBrokenClients until LateJoinTimeoutSeconds passes.
        public static readonly Dictionary<NetworkConnection, float> PendingConnections =
            new Dictionary<NetworkConnection, float>();

        // Direct-IP clients initially send Ready while still in the treehouse.
        // The host ignores that first Ready and accepts the second one after the
        // server-directed level scene has loaded.
        public static readonly HashSet<NetworkConnection> DeferredReadyConnections =
            new HashSet<NetworkConnection>();

        // Network numbers (1-based lobby slots) protected from the vanilla
        // "no character picked" purge/kick paths while the joiner is undecided or
        // spectating. Maintained on every peer (relayed state).
        public static readonly HashSet<int> ProtectedNumbers = new HashSet<int>();

        // Host side: picks accepted mid-level (networkNumber -> animal) awaiting
        // activation at the next ToPlaceMode.
        public static readonly Dictionary<int, int> PendingPicks = new Dictionary<int, int>();

        // Host side: requested entry mode per waiting joiner
        // (networkNumber -> 0 = play, 1 = spectate). An entry also marks the
        // joiner as "not yet activated" for score refreshes.
        public static readonly Dictionary<int, byte> JoinerModes = new Dictionary<int, byte>();

        // Joiner side: last phase/round snapshot received from the host.
        public static MsgLateJoinGameState LastGameState;

        // How long a mid-level joiner may take to load the level scene before the
        // vanilla broken-client cleanup may reclaim the connection.
        public const float LateJoinTimeoutSeconds = 90f;

        public static bool Enabled
        {
            get { return MorePlayersMod.lateJoinEnabled != null && MorePlayersMod.lateJoinEnabled.Value; }
        }

        public static bool IsProtected(int networkNumber)
        {
            return ProtectedNumbers.Contains(networkNumber);
        }

        public static void Reset()
        {
            ClientJoiningLate = false;
            ClientIntegrated = false;
            PendingConnections.Clear();
            DeferredReadyConnections.Clear();
            ProtectedNumbers.Clear();
            PendingPicks.Clear();
            JoinerModes.Clear();
            LastGameState = null;
        }
    }
}
