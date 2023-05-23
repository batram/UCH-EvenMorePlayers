using BepInEx;
using HarmonyLib;
using InControl;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.Match;

[assembly: AssemblyVersion("0.9.0.2")]
[assembly: AssemblyInformationalVersion("0.9.0.2")]

namespace MorePlayers
{
    [BepInPlugin("notfood.MorePlayers", "EvenMorePlayers", "0.9.0.2")]
    public class MorePlayersMod : BaseUnityPlugin
    {
        public const int newPlayerLimit = 100;

        public static bool fullDebug = true;
        public static string og_version;
        public static string mod_version = "0.9.0.2";
        public static string mod_version_full = " [EvenMorePlayers: " + mod_version + "]";

        void Awake()
        {
            og_version = GameSettings.GetInstance().versionNumber;
            PlayerManager.maxPlayers = newPlayerLimit;
            new Harmony("notfood.MorePlayers.PlayerNumPatch").PatchAll();
            MenuPatch.PatchMenu();
            Debug.Log("[MorePlayersMod] started.");
        }
    }

    [HarmonyPatch]
    static class Switch4ForMaxNumPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(BeeSwarm), nameof(BeeSwarm.Awake));
            yield return AccessTools.Method(typeof(ChallengeScoreboard), nameof(ChallengeScoreboard.CollectPlayerIds));
            yield return AccessTools.Method(typeof(Controller), nameof(Controller.AddPlayer));
            yield return AccessTools.Method(typeof(Controller), nameof(Controller.AssociateCharacter));
            yield return AccessTools.Method(typeof(ControllerDisconnect), nameof(ControllerDisconnect.SetPromptForPlayer));
            yield return AccessTools.Method(typeof(GraphScoreBoard), nameof(GraphScoreBoard.SetPlayerCount));
            yield return AccessTools.Method(typeof(InventoryBook), nameof(InventoryBook.AddPlayer));
            yield return AccessTools.Method(typeof(InventoryBook), nameof(InventoryBook.GetCursor));
            yield return AccessTools.Method(typeof(InventoryBook), nameof(InventoryBook.HasCursor));
            yield return AccessTools.Method(typeof(KeyboardInput), nameof(KeyboardInput.Reset));
            yield return AccessTools.Constructor(typeof(KickTracker));
            yield return AccessTools.Method(typeof(KickTracker), nameof(KickTracker.ClearPlayer));
            yield return AccessTools.Method(typeof(KickTracker), nameof(KickTracker.CountVotes));
            yield return AccessTools.Method(typeof(KickTracker), nameof(KickTracker.VotesFromNetworkNumber));
            yield return AccessTools.Method(typeof(LobbyPointCounter), nameof(LobbyPointCounter.handleEvent));
            yield return AccessTools.Method(typeof(PartyBox), nameof(PartyBox.SetPlayerCount));
            yield return AccessTools.Method(typeof(PickableNetworkButton), nameof(PickableNetworkButton.OnAccept));
            yield return AccessTools.Method(typeof(PlayerStatusDisplay), nameof(PlayerStatusDisplay.SetSlotCount));
            yield return AccessTools.Method(typeof(StatTracker), nameof(StatTracker.GetSaveFileDataForLocalPlayer));
            yield return AccessTools.Method(typeof(StatTracker), nameof(StatTracker.OnLocalPlayerAdded));
            yield return AccessTools.Method(typeof(StatTracker), nameof(StatTracker.SaveGameForAnimal));
            yield return AccessTools.Method(typeof(SteamLobbySearchList), nameof(SteamLobbySearchList.checkForListUpdates));
            yield return AccessTools.Method(typeof(SteamMatchmaker), nameof(SteamMatchmaker.createSocialLobby));
            yield return AccessTools.Method(typeof(SwitchController), nameof(SwitchController.Reset));
            yield return AccessTools.Method(typeof(TurnIndicator), nameof(TurnIndicator.SetPlayerCount));
            yield return AccessTools.Method(typeof(UnityMatchmaker), nameof(UnityMatchmaker.CheckHostConnectivity));
            //yield return AccessTools.Method(typeof(UnityMatchmaker), nameof(UnityMatchmaker.CreateUnityMatch));
            yield return AccessTools.Method(typeof(VersusControl), "get_playersLeftToPlace");            
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e)
        {
            foreach (var inst in e)
            {
                if (inst.opcode == OpCodes.Ldc_I4_4)
                {
                    inst.opcode = OpCodes.Ldc_I4;
                    inst.operand = PlayerManager.maxPlayers;
                }
                yield return inst;
            }
        }
    }

    [HarmonyPatch]
    static class SwitchFirst4ForMaxNumPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PartyBox), nameof(PartyBox.AddPlayer));
            yield return AccessTools.Method(typeof(LobbySkillTracker), nameof(LobbySkillTracker.RecalculateScores));
            yield return AccessTools.Method(typeof(PickableNetworkButton), nameof(PickableNetworkButton.Update));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e)
        {
            var count = 0;

            foreach (var inst in e)
            {
                if (inst.opcode == OpCodes.Ldc_I4_4 && count == 0)
                {
                    inst.opcode = OpCodes.Ldc_I4;
                    inst.operand = PlayerManager.maxPlayers;
                    count += 1;
                }
                yield return inst;
            }
        }
    }

    [HarmonyPatch]
    static class SwitchSecond4ForMaxNumPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(LobbySkillTracker), nameof(LobbySkillTracker.UpdateLobbyInfo));
            yield return AccessTools.Method(typeof(UnityMatchmaker), nameof(UnityMatchmaker.onLobbyJoined));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e)
        {
            var count = 0;

            foreach (var inst in e)
            {
                if (inst.opcode == OpCodes.Ldc_I4_4)
                {
                    if (count == 1)
                    {
                        inst.opcode = OpCodes.Ldc_I4;
                        inst.operand = PlayerManager.maxPlayers;
                    }
                    count += 1;
                }
                yield return inst;
            }
        }
    }

    [HarmonyPatch]
    static class Switch5ForNumPlusOnePatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SteamMatchmaker), nameof(SteamMatchmaker.OnSteamLobbyJoinRequested));
            yield return AccessTools.Method(typeof(LobbyManager), nameof(LobbyManager.OnLobbyClientAddPlayerFailed));
            yield return AccessTools.Method(typeof(Matchmaker), nameof(Matchmaker.CleanUpPlayers));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e)
        {
            foreach (var inst in e)
            {
                if (inst.opcode == OpCodes.Ldc_I4_4)
                {
                    inst.opcode = OpCodes.Ldc_I4;
                    inst.operand = PlayerManager.maxPlayers + 1;
                }
                yield return inst;
            }
        }
    }

    [HarmonyPatch]
    static class Switch3ForNumMinusOnePatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Controller), nameof(Controller.GetLastPlayerNumber));
            yield return AccessTools.Method(typeof(Controller), nameof(Controller.GetLastPlayerNumberAfter));

            yield return AccessTools.Method(typeof(GraphScoreBoard), nameof(GraphScoreBoard.SetPlayerCharacter));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e)
        {
            foreach (var inst in e)
            {
                if (inst.opcode == OpCodes.Ldc_I4_3)
                {
                    inst.opcode = OpCodes.Ldc_I4;
                    inst.operand = PlayerManager.maxPlayers - 1;
                }
                yield return inst;
            }
        }
    }

    [HarmonyPatch(typeof(ChallengeScoreboard), MethodType.Constructor)]
    static class ChallengeScoreboardCtorPatch
    {
        static void Postfix(ChallengeScoreboard __instance)
        {
            __instance.players = new ChallengeScoreboard.ChallengePlayer[PlayerManager.maxPlayers];
        }
    }

    [HarmonyPatch(typeof(Tablet), MethodType.Constructor)]
    static class TabletCtorPatch
    {
        static void Postfix(Tablet __instance)
        {
            __instance.untrackedCursors = new List<PickCursor>(PlayerManager.maxPlayers);
        }
    }

    [HarmonyPatch(typeof(Controller), MethodType.Constructor)]
    static class ControllerCtorPatch
    {
        static void Postfix(Controller __instance)
        {
            __instance.associatedChars = new Character.Animals[PlayerManager.maxPlayers];
        }
    }

    [HarmonyPatch(typeof(Controller), nameof(Controller.ClearPlayers))]
    static class ControllerClearPlayersPatch
    {
        static void Postfix(Controller __instance)
        {
            __instance.associatedChars = new Character.Animals[PlayerManager.maxPlayers];
        }
    }

    [HarmonyPatch(typeof(Controller), nameof(Controller.RemovePlayer))]
    static class ControllerRemovePlayerPatch
    {
        static bool Prefix(Controller __instance, int player)
        {
            // remove player bit from bitmask
            var num = ~(1 << (player - 1));
            __instance.Player &= num;
            __instance.associatedChars[player - 1] = Character.Animals.NONE;
            if (__instance.Player == 0)
            {
                __instance.PossibleNetWorkNumber = 0;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(GameState), MethodType.Constructor)]
    static class GameStateCtorPatch
    {
        static void Postfix(GameState __instance)
        {
            __instance.PlayerScores = new int[PlayerManager.maxPlayers];
        }
    }

    [HarmonyPatch(typeof(GraphScoreBoard), nameof(GraphScoreBoard.SetPlayerCount))]
    static class GraphScoreBoardCtorPatch
    {
        static bool Prefix(GraphScoreBoard __instance, int numberPlayers)
        {
            Array.Resize<RectTransform>(ref __instance.ScorePositions, PlayerManager.maxPlayers);

            __instance.playerScoreLines = new ScoreLine[numberPlayers];

            Debug.Log("GraphScoreBoard.SetPlayerCount");
            Vector3 vector = __instance.ScorePositions[0].position + new Vector3(0f, 1.25f, 0f);
            for (int num = 0; num != numberPlayers; num++)
            {
                /* add ScorePositions for additional players */
                GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(__instance.scoreLinePrefab.gameObject, vector - new Vector3(0f, (float)num * 1.25f, 0f), Quaternion.identity);
                gameObject.transform.SetParent(__instance.mainParent);
                gameObject.transform.localScale = new Vector3(1f, 0.5f, 1f);
                __instance.playerScoreLines[num] = gameObject.GetComponent<ScoreLine>();
                __instance.playerScoreLines[num].scoreBoardParent = __instance;
            }

            // skip function
            return false;
        }
    }

    [HarmonyPatch(typeof(LevelSelectController), nameof(LevelSelectController.Awake))]
    static class LevelSelectControllerCtorPatch
    {
        static void Postfix(LevelSelectController __instance)
        {
            Debug.Log("fixed LevelSelectController");
            __instance.JoinedPlayers = new LobbyPlayer[PlayerManager.maxPlayers];

            if (__instance.PlayerJoinIndicators.Length < PlayerManager.maxPlayers)
            {
                int num = __instance.PlayerJoinIndicators.Length;
                Array.Resize<playerJoinIndicator>(ref __instance.PlayerJoinIndicators, PlayerManager.maxPlayers);
                for (int i = num; i < __instance.PlayerJoinIndicators.Length; i++)
                {
                    /* map PlayerJoinIndicators to 0..3, we reuse existing 4 indicators for multiple players */
                    __instance.PlayerJoinIndicators[i] = __instance.PlayerJoinIndicators[i % num];
                }
            }

            Debug.Log("CursorSpawnPoint.Length " + __instance.CursorSpawnPoint.Length);
            if (__instance.CursorSpawnPoint.Length < PlayerManager.maxPlayers)
            {
                int num = __instance.CursorSpawnPoint.Length;
                Array.Resize(ref __instance.CursorSpawnPoint, PlayerManager.maxPlayers);
                for (int i = num; i < __instance.CursorSpawnPoint.Length; i++)
                {
                    /* map CursorSpawnPoint to 0..3
                        TODO: add new positions, so cursors aren't hidden behind each other on spawn
                     */
                    __instance.CursorSpawnPoint[i] = __instance.CursorSpawnPoint[i % num];
                }
            }

            Debug.Log("UndergroundCharacterPosition.Length " + __instance.UndergroundCharacterPosition.Length);
            if (__instance.UndergroundCharacterPosition.Length < PlayerManager.maxPlayers)
            {
                int num = __instance.UndergroundCharacterPosition.Length;
                Array.Resize(ref __instance.UndergroundCharacterPosition, PlayerManager.maxPlayers);
                for (int i = num; i < __instance.UndergroundCharacterPosition.Length; i++)
                {
                    /* map UndergroundCharacterPosition to 0..3
                        TODO: add new positions
                     */
                    __instance.UndergroundCharacterPosition[i] = __instance.UndergroundCharacterPosition[i % num];
                }
            }
        }
    }

    [HarmonyPatch(typeof(LobbyPointCounter), MethodType.Constructor)]
    static class LobbyPointCounterCtorPatch
    {
        static void Postfix(LobbyPointCounter __instance)
        {
            __instance.playerJoinedGame = new bool[PlayerManager.maxPlayers];
            __instance.playerPlayedGame = new bool[PlayerManager.maxPlayers];
            __instance.playerAFK = new bool[PlayerManager.maxPlayers];
        }
    }

    [HarmonyPatch(typeof(LobbyPointCounter), nameof(LobbyPointCounter.Reset))]
    static class LobbyPointCounterResetCtorPatch
    {
        static void Postfix(LobbyPointCounter __instance)
        {
            __instance.playerPlayedGame = new bool[PlayerManager.maxPlayers];
        }
    }

    [HarmonyPatch(typeof(NetworkLobbyManager), MethodType.Constructor)]
    static class NetworkLobbyManagerCtorPatch
    {
        static void Postfix(NetworkLobbyManager __instance)
        {
            __instance.maxPlayers = PlayerManager.maxPlayers;
        }
    }


    [HarmonyPatch(typeof(LobbySkillTracker), nameof(LobbySkillTracker.Start))]
    static class LobbySkillTrackerCtorPatch
    {
        static void Postfix(LobbySkillTracker __instance)
        {
            Debug.Log("LobbySkillTracker patch " + __instance.ratings.Length);
            __instance.ratings = new Moserware.Skills.Rating[PlayerManager.maxPlayers];
        }
    }

    [HarmonyPatch(typeof(VersusControl), MethodType.Constructor)]
    static class VersusControlCtorPatch
    {
        static void Postfix(VersusControl __instance)
        {
            Debug.Log("patch VersusControl " + PlayerManager.maxPlayers);
            __instance.winOrder = new GamePlayer[PlayerManager.maxPlayers];
            __instance.RemainingPlacements = new int[PlayerManager.maxPlayers];
        }
    }

    [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.Awake))]
    static class LobbyManagerCtorPatch
    {
        static void Postfix(LobbyManager __instance)
        {
            Debug.Log("LobbyManager.instance.lobbySlots " + __instance.lobbySlots.Length);

            __instance.maxPlayers = PlayerManager.maxPlayers;
            __instance.maxPlayersPerConnection = PlayerManager.maxPlayers;

            if (__instance.lobbySlots.Length < PlayerManager.maxPlayers)
            {
                Array.Resize(ref __instance.lobbySlots, PlayerManager.maxPlayers);
            }
        }
    }

    [HarmonyPatch(typeof(GameSettings), nameof(GameSettings.GetInstance))]
    static class GameSettingsCtorPatch
    {
        static void Postfix(GameSettings __result)
        {
            __result.MaxPlayers = PlayerManager.maxPlayers;

            if (__result.PlayerColors.Length < PlayerManager.maxPlayers)
            {
                int num2 = __result.PlayerColors.Length;
                Array.Resize<Color>(ref __result.PlayerColors, PlayerManager.maxPlayers);
                Color[] playerColors = __result.PlayerColors;
                for (int j = num2; j < playerColors.Length; j++)
                {
                    /* Random generate colors for new players */
                    __result.PlayerColors[j] = new Color(UnityEngine.Random.Range(0f, 1f), UnityEngine.Random.Range(0f, 1f), UnityEngine.Random.Range(0f, 1f));
                }
            }

        }
    }

    [HarmonyPatch(typeof(InventoryBook), nameof(InventoryBook.AddPlayer))]
    static class InventoryBookCtorPatch
    {
        static void Prefix(InventoryBook __instance)
        {
            if (__instance.cursorSpawnLocation.Length < PlayerManager.maxPlayers)
            {
                int num2 = __instance.cursorSpawnLocation.Length;
                Array.Resize<Transform>(ref __instance.cursorSpawnLocation, PlayerManager.maxPlayers);
                for (int j = num2; j < __instance.cursorSpawnLocation.Length; j++)
                {
                    __instance.cursorSpawnLocation[j] = __instance.cursorSpawnLocation[0];
                }

            }
            Debug.Log("InventoryBook cursorSpawnLocation patched");
        }
    }

    [HarmonyPatch(typeof(ControllerDisconnect), nameof(ControllerDisconnect.Start))]
    static class ControllerDisconnectCtorPatch
    {
        static void Prefix(ControllerDisconnect __instance)
        {
            var oglen = __instance.ConnectPrompts.Length;
            Array.Resize<XboxReconnectPrompt>(ref __instance.ConnectPrompts, PlayerManager.maxPlayers);

            for (var i = oglen; i < PlayerManager.maxPlayers; i++)
            {
                __instance.ConnectPrompts[i] = __instance.ConnectPrompts[0];
            }

            if (__instance.orphanedReceivers.Length != PlayerManager.maxPlayers)
            {
                var og_len = __instance.orphanedReceivers.Length;
                Array.Resize(ref __instance.orphanedReceivers, PlayerManager.maxPlayers);
                for (var i = og_len; i < __instance.orphanedReceivers.Length; i++)
                {
                    __instance.orphanedReceivers[i] = new List<InputReceiver>();
                }
            }
            ControllerDisconnect.showingPrompts = new bool[PlayerManager.maxPlayers];
            __instance.orphanedCharacters = new Character.Animals[PlayerManager.maxPlayers][];

            Debug.Log("ControllerDisconnect patched");
        }
    }

    [HarmonyPatch(typeof(InputManager), "get_EnableNativeInput")]
    static class InputManagerCtorPatch
    {
        static void Postfix(ref bool __result)
        {
            Debug.Log("InputManager.EnableNativeInput");
            __result = true;
        }
    }

    [HarmonyPatch(typeof(LevelPortal), nameof(LevelPortal.Awake))]
    static class LevelPortalCtorPatch
    {
        static void Prefix(LevelPortal __instance)
        {
            VoteArrow[] componentsInChildren = __instance.GetComponentsInChildren<VoteArrow>();
            Debug.Log("VoteArrows " + componentsInChildren.Length);
            if (componentsInChildren.Length != PlayerManager.maxPlayers)
            {
                int num = componentsInChildren.Length;

                for (int j = num; j < PlayerManager.maxPlayers; j++)
                {
                    Type type = componentsInChildren[3].GetType();
                    VoteArrow voteArrow2 = componentsInChildren[3].gameObject.AddComponent(type) as VoteArrow;
                    foreach (FieldInfo fieldInfo in type.GetFields())
                    {
                        fieldInfo.SetValue(voteArrow2, fieldInfo.GetValue(componentsInChildren[3]));
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(StatTracker), MethodType.Constructor)]
    static class StatTrackerCtorPatch
    {
        static void Postfix(StatTracker __instance)
        {
            Debug.Log("patch StatTracker " + PlayerManager.maxPlayers);
            __instance.saveFiles = new SaveFileData[PlayerManager.maxPlayers];
            __instance.saveStatuses = new StatTracker.SaveFileStatus[PlayerManager.maxPlayers];
        }
    }

    [HarmonyPatch(typeof(GameSparksQuery), nameof(GameSparksQuery.DoGetLobbyData))]
    static class GameSparksQueryLobbyCtorPatch
    {
        static void Prefix(ref bool reserveSlot)
        {
            Debug.Log("reserveSlot " + reserveSlot);
            reserveSlot = false;
        }
    }

    [HarmonyPatch(typeof(LivesDisplayController), nameof(LivesDisplayController.Initialize))]
    static class LivesDisplayControllerCtorPatch
    {
        static void Prefix(LivesDisplayController __instance)
        {
            Debug.Log("LivesDisplayController patch " + __instance.livesDisplayBoxes.Count);
            if (__instance.livesDisplayBoxes.Count < PlayerManager.maxPlayers)
            {
                var og_len = __instance.livesDisplayBoxes.Count;
                for (var i = og_len; i <= PlayerManager.maxPlayers; i++)
                {
                    __instance.livesDisplayBoxes.Add(__instance.livesDisplayBoxes[0]);
                }

            }
        }
    }

    [HarmonyPatch(typeof(PlayerStatusDisplay), nameof(PlayerStatusDisplay.SetupSlot))]
    [HarmonyPatch(typeof(PlayerStatusDisplay), nameof(PlayerStatusDisplay.SetSlot))]
    [HarmonyPatch(typeof(PlayerStatusDisplay), nameof(PlayerStatusDisplay.SetSlotCount))]
    static class PlayerStatusDisplaySetSlotCountCtorPatch
    {
        static void Prefix(PlayerStatusDisplay __instance)
        {
            Debug.Log("PlayerStatusDisplay patch " + __instance.Slots.Length);
            if (__instance.Slots.Length < PlayerManager.maxPlayers)
            {
                var og_len = __instance.Slots.Length;
                Array.Resize<StatusSlot>(ref __instance.Slots, PlayerManager.maxPlayers);
                for (var i = og_len; i < PlayerManager.maxPlayers; i++)
                {
                    __instance.Slots[i] = __instance.Slots[0];
                }

            }
        }
    }

    [HarmonyPatch(typeof(VersusControl), nameof(VersusControl.ShuffleStartPosition))]
    static class VersusControlxCtorPatch
    {
        static void Postfix(VersusControl __instance)
        {
            Debug.Log("check VersusControl RandomStartPositionString " + __instance.RandomStartPositionString + " Length " + __instance.RandomStartPositionString.Length);
            var rstr = "";
            for (int j = 0; j < __instance.PlayerQueue.Count; j++)
            {
                int rando = UnityEngine.Random.Range(0, 8);
                rstr += rando.ToString();
            }

            __instance.NetworkRandomStartPositionString = rstr;
            Debug.Log("check VersusControl RandomStartPositionString " + __instance.RandomStartPositionString + " Length " + __instance.RandomStartPositionString.Length);
        }
    }

    [HarmonyPatch(typeof(NetworkManager), nameof(NetworkManager.StartServer), new Type[] {typeof(ConnectionConfig), typeof(int) })]
    static class NetworkManagerCtorPatch
    {
        static void Prefix(NetworkManager __instance, int maxConnections)
        {
            Debug.Log("NetworkManager StartServer " + __instance.maxConnections + " param maxConnections " + maxConnections);
            __instance.maxConnections = PlayerManager.maxPlayers;
        }
    }

    [HarmonyPatch(typeof(PickableNetworkButton), nameof(PickableNetworkButton.SetSearchResultInfo))]
    static class PickableNetworkButtonCtorPatch
    {
        static void Postfix(PickableNetworkButton __instance, Matchmaker.LobbyListInfo lobbyInfo)
        {
            __instance.NumPlayersText.text = lobbyInfo.Players.ToString() + "/?";
        }
    }

    [HarmonyPatch(typeof(GameControl), nameof(GameControl.Awake))]
    static class GameControlCtorPatch
    {
        static void Postfix(GameControl __instance)
        {
            if (__instance.showScoreButtons.Length < PlayerManager.maxPlayers)
            {
                Array.Resize(ref __instance.showScoreButtons, PlayerManager.maxPlayers);
            }
        }
    }

    [HarmonyPatch(typeof(GameControl), nameof(GameControl.ReceiveEvent))]
    static class GameControlReceiveEventCtorPatch
    {
        static void Prefix(GameControl __instance, InputEvent e)
        {

            __instance.inputPlayerNumber = 0;
            //TODO: Fix overflow int for more than 94 players
            for (var i = 0; i < PlayerManager.maxPlayers && i < 95; i++)
            {
                if ((e.PlayerBitMask & (1 << i)) == (1 << i))
                {
                    __instance.inputPlayerNumber = i + 1;
                    break;
                }
            }
        }
    }

    [HarmonyPatch]
    static class GameControlReceiveEventDropInputPlayerNumber0Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GameControl), nameof(GameControl.ReceiveEvent));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e)
        {
            var done = false;

            foreach (var inst in e)
            {
                if (!done && inst.opcode != OpCodes.Ldarg_1)
                {
                    // NOP out this.inputPlayerNumber = 0;
                    inst.opcode = OpCodes.Nop;
                }
                else
                {
                    done = true;
                }
                yield return inst;
            }
        }
    }

}