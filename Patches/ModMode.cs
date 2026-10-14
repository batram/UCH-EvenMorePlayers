using HarmonyLib;
using UnityEngine;

namespace MorePlayers
{
    // Single place that switches between modded and vanilla online state.
    // Modded: modded version string (own matchmaking pool) + player number patches.
    // Vanilla: original version string + all [HarmonyPatch] classes of this assembly removed.
    static class ModMode
    {
        public const string PlayerNumPatchId = "EvenMorePlayers.PlayerNumPatch";

        // GameSettings.MaxPlayers as shipped, read before any patch is applied
        public static int VanillaMaxPlayers = 4;

        public static bool PatchesActive
        {
            get { return Harmony.HasAnyPatches(PlayerNumPatchId); }
        }

        public static bool VersionModded
        {
            get { return GameSettings.GetInstance().versionNumber.StartsWith(MorePlayersMod.mod_version_full); }
        }

        // MatchmakingNumber the game reports with the original version, without debug suffix
        public static string VanillaMatchmakingNumber
        {
            get
            {
                var hparts = MorePlayersMod.og_version.Split('.');
                return hparts[0] + "." + hparts[1];
            }
        }

        // MatchmakingNumber in modded mode: drop mod patch number, so players on same minor version can play together
        public static string ModdedMatchmakingNumber
        {
            get
            {
                var vparts = MorePlayersMod.mod_version_full.Split('-');
                return vparts[0] + "-" + vparts[1] + "_" + VanillaMatchmakingNumber;
            }
        }

        public static void EnableModded()
        {
            if (!VersionModded)
            {
                var settings = GameSettings.GetInstance();
                settings.versionNumber = MorePlayersMod.mod_version_full + "_" + MorePlayersMod.og_version;
                settings.parsedMatchmakingNumber = ModdedMatchmakingNumber;
                settings.parsedVersionNumberProd = null;
            }
            EnablePatches();
            Debug.Log("[MorePlayersMod] modded mode, matchmaking " + GameSettings.GetInstance().MatchmakingNumber);
        }

        public static void EnableVanilla()
        {
            var settings = GameSettings.GetInstance();
            settings.versionNumber = MorePlayersMod.og_version;
            // Reset parsedMatchmakingNumber to force recalculation
            settings.parsedMatchmakingNumber = null;
            settings.parsedVersionNumberProd = null;

            if (PatchesActive)
            {
                Harmony.UnpatchID(PlayerNumPatchId);
            }
            PlayerManager.maxPlayers = VanillaMaxPlayers;
            ResetPersistentState();
            MoreCode.CleanGUI();
            Debug.Log("[MorePlayersMod] vanilla mode, matchmaking " + settings.MatchmakingNumber);
        }

        // Unpatching does not undo what the patches wrote into state that outlives a scene.
        // Arrays that vanilla only indexes by player number can stay larger, these two are
        // iterated by length and would carry the modded player count into a vanilla session.
        static void ResetPersistentState()
        {
            // GameSettings.GetInstance postfix raised it; vanilla LobbyPlayer registers local players
            // up to MaxPlayers and VersusControl.Scores sizes the score array with it
            GameSettings.GetInstance().MaxPlayers = VanillaMaxPlayers;

            // ControllerDisconnect.Start prefix grew this static; vanilla Start walks its length
            // over the 4 ConnectPrompts and throws IndexOutOfRangeException
            if (ControllerDisconnect.showingPrompts != null && ControllerDisconnect.showingPrompts.Length != VanillaMaxPlayers)
            {
                System.Array.Resize(ref ControllerDisconnect.showingPrompts, VanillaMaxPlayers);
            }
        }

        // Player number patches without touching the version, used for local play
        public static void EnablePatches()
        {
            if (!PatchesActive)
            {
                // transpilers bake maxPlayers into the IL, so it has to be set before patching
                PlayerManager.maxPlayers = MorePlayersMod.newPlayerLimit.Value;
                new Harmony(PlayerNumPatchId).PatchAll();
                RestorePersistentState();
            }
        }

        // The other direction: state that outlives a scene and was reset to vanilla size while
        // unpatched. Controllers persist for the whole session and vanilla ClearPlayers (called on
        // every disconnect) recreates associatedChars with 4 slots, so the 5th local player's
        // AssociateCharacter throws IndexOutOfRangeException after any vanilla round trip
        static void RestorePersistentState()
        {
            foreach (var controller in Controller.allControllers)
            {
                if (controller != null && controller.associatedChars != null && controller.associatedChars.Length < PlayerManager.maxPlayers)
                {
                    System.Array.Resize(ref controller.associatedChars, PlayerManager.maxPlayers);
                }
            }
        }

        // Lobby version strings carry the debug suffix the same way MatchmakingNumber does
        static bool SameMatchmaking(string lobbyVersion, string matchmakingNumber)
        {
            if (GameSettings.Debug && GameSettings.GetInstance().DebugOutsideEditor)
            {
                matchmakingNumber += "d";
            }
            return lobbyVersion == matchmakingNumber;
        }

        // Switch into the mode the host's lobby was created with. Returns false if the
        // lobby version is unknown to us (other game or mod version), mode is left untouched
        // and the game reports the version mismatch as usual.
        public static bool FollowHostVersion(string lobbyVersion)
        {
            if (string.IsNullOrEmpty(lobbyVersion))
            {
                Debug.Log("[MorePlayersMod] host lobby version unknown, keeping current mode");
                return false;
            }
            if (SameMatchmaking(lobbyVersion, ModdedMatchmakingNumber))
            {
                Debug.Log("[MorePlayersMod] host lobby is modded (" + lobbyVersion + ")");
                EnableModded();
                return true;
            }
            if (SameMatchmaking(lobbyVersion, VanillaMatchmakingNumber))
            {
                Debug.Log("[MorePlayersMod] host lobby is vanilla (" + lobbyVersion + ")");
                EnableVanilla();
                return true;
            }
            Debug.Log("[MorePlayersMod] host lobby version " + lobbyVersion + " matches neither "
                + ModdedMatchmakingNumber + " nor " + VanillaMatchmakingNumber + ", keeping current mode");
            return false;
        }
    }
}
