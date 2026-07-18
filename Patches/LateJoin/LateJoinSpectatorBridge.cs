using System;

namespace MorePlayers.LateJoin
{
    // The single touch point between late-join and the spectator-couch system.
    // The spectator code (being reworked separately) wires these delegates from
    // its side; late-join never references spectator internals directly.
    public static class LateJoinSpectatorBridge
    {
        // Invoked with the joiner's networkNumber when a late joiner who chose
        // "spectate" reaches the treehouse. Null while spectator mode is
        // unavailable -> late-join falls back to a normal unpicked lobby player.
        public static Action<int> OnLateJoinerWantsSpectate;

        // Optional query so late-join knows when the handoff completed and can
        // drop its purge protection for that networkNumber.
        public static Func<int, bool> IsSpectating;
    }
}
