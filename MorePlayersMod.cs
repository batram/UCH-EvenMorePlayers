using System.Collections.Generic;
using System;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using InControl;

namespace MorePlayers
{
    [BepInPlugin("notfood.MorePlayers", "EvenMorePlayers", "0.0.0.1")]
    public class MorePlayersMod : BaseUnityPlugin
    {
        void Awake()
        {
            PlayerManager.maxPlayers = 8;

            new Harmony("notfood.UltimateBuilder").PatchAll();

            Debug.Log("[MorePlayersMod] started.");
            Debug.Log("[GameSettings.GetInstance().MaxPlayers] " + GameSettings.GetInstance().MaxPlayers + "; [PlayerManager.maxPlayers] " + PlayerManager.maxPlayers);
        }
    }

    [HarmonyPatch]
    static class Switch4For8Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ChallengeScoreboard), nameof(ChallengeScoreboard.CollectPlayerIds));
            yield return AccessTools.Method(typeof(UnityMatchmaker), nameof(UnityMatchmaker.CreateUnityMatch));
            yield return AccessTools.Method(typeof(Controller), nameof(Controller.AssociateCharacter));
            yield return AccessTools.Method(typeof(TurnIndicator), nameof(TurnIndicator.SetPlayerCount));
            yield return AccessTools.Method(typeof(SwitchController), nameof(SwitchController.Reset));
            yield return AccessTools.Constructor(typeof(KickTracker));
            yield return AccessTools.Method(typeof(KickTracker), nameof(KickTracker.ClearPlayer));
            yield return AccessTools.Method(typeof(KickTracker), nameof(KickTracker.CountVotes));
            yield return AccessTools.Method(typeof(KickTracker), nameof(KickTracker.VotesFromNetworkNumber));
            yield return AccessTools.Method(typeof(KeyboardInput), nameof(KeyboardInput.Reset));
            yield return AccessTools.Method(typeof(VersusControl), "get_playersLeftToPlace"); 

            yield return AccessTools.Method(typeof(Controller), nameof(Controller.AddPlayer));
            yield return AccessTools.Method(typeof(ControllerDisconnect), nameof(ControllerDisconnect.SetPromptForPlayer));
            yield return AccessTools.Method(typeof(GraphScoreBoard), nameof(GraphScoreBoard.SetPlayerCount));

            yield return AccessTools.Method(typeof(InventoryBook), nameof(InventoryBook.HasCursor));
            yield return AccessTools.Method(typeof(InventoryBook), nameof(InventoryBook.GetCursor));
            yield return AccessTools.Method(typeof(InventoryBook), nameof(InventoryBook.AddPlayer));

            yield return AccessTools.Method(typeof(PartyBox), nameof(PartyBox.SetPlayerCount));
        }
        
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e)
        {
            foreach(var inst in e)
            {
                if (inst.opcode == OpCodes.Ldc_I4_4)
                {
                    inst.opcode = OpCodes.Ldc_I4_8;
                }
                yield return inst;
            }
        }
    }

    [HarmonyPatch]
    static class SwitchFirst4For8Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PartyBox), nameof(PartyBox.AddPlayer));
        }
        
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e)
        {
            var count = 0;

            foreach(var inst in e)
            {
                if (inst.opcode == OpCodes.Ldc_I4_4 && count == 0)
                {
                    inst.opcode = OpCodes.Ldc_I4_8;
                    count += 1;
                }
                yield return inst;
            }
        }
    }


    [HarmonyPatch]
    static class Switch3For7Patch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Controller), nameof(Controller.GetLastPlayerNumber));
            yield return AccessTools.Method(typeof(Controller), nameof(Controller.GetLastPlayerNumberAfter));

            yield return AccessTools.Method(typeof(GraphScoreBoard), nameof(GraphScoreBoard.SetPlayerCharacter));
        }
        
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> e)
        {
            foreach(var inst in e)
            {
                if (inst.opcode == OpCodes.Ldc_I4_3)
                {
                    inst.opcode = OpCodes.Ldc_I4_7;
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
            __instance.players = new ChallengeScoreboard.ChallengePlayer[8];
        }
    }

    [HarmonyPatch(typeof(Tablet), MethodType.Constructor)]
    static class TabletCtorPatch
    {
        static void Postfix(Tablet __instance)
        {
            __instance.untrackedCursors = new List<PickCursor>(8);
        }
    }

    [HarmonyPatch(typeof(Controller), MethodType.Constructor)]
    static class ControllerCtorPatch
    {
        static void Postfix(Controller __instance)
        {
            __instance.associatedChars = new Character.Animals[8];
        }
    }

    [HarmonyPatch(typeof(Controller), nameof(Controller.ClearPlayers))]
    static class ControllerClearPlayersPatch
    {
        static void Postfix(Controller __instance)
        {
            __instance.associatedChars = new Character.Animals[8];
        }
    }

    [HarmonyPatch(typeof(GameState), MethodType.Constructor)]
    static class GameStateCtorPatch
    {
        static void Postfix(GameState __instance)
        {
            __instance.PlayerScores = new int[8];
        }
    }

    [HarmonyPatch(typeof(GraphScoreBoard), nameof(GraphScoreBoard.SetPlayerCount))]
    static class GraphScoreBoardCtorPatch
    {
        static bool Prefix(GraphScoreBoard __instance, int numberPlayers)
        {
            Array.Resize<RectTransform>(ref __instance.ScorePositions, PlayerManager.maxPlayers);

            __instance.playerScoreLines = new ScoreLine[numberPlayers];

            Debug.LogError("GraphScoreBoard.SetPlayerCount");
            Vector3 vector = __instance.ScorePositions[0].position + new Vector3(0f, 1.25f, 0f);
            for (int num = 0; num != numberPlayers; num++)
            {
                /* add ScorePositions for additional players */
                GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(__instance.scoreLinePrefab.gameObject, vector - new Vector3(0f, (float)num * 1.25f, 0f), Quaternion.identity);
                gameObject.transform.SetParent(__instance.mainParent);
                gameObject.transform.localScale = Vector3.one;
                __instance.playerScoreLines[num] = gameObject.GetComponent<ScoreLine>();
                __instance.playerScoreLines[num].scoreBoardParent = __instance;
            }

            // skip function
            return false;
        }

        static void Postfix(GraphScoreBoard __instance, int numberPlayers)
        {
        }
    }

    [HarmonyPatch(typeof(LevelSelectController), nameof(LevelSelectController.Awake))]
    static class LevelSelectControllerCtorPatch
    {
        static void Postfix(LevelSelectController __instance)
        {
            Debug.Log("fixed LevelSelectController");
            __instance.JoinedPlayers = new LobbyPlayer[8];
            
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

        }

    }

    [HarmonyPatch(typeof(LobbyPointCounter), MethodType.Constructor)]
    static class LobbyPointCounterCtorPatch
    {
        static void Postfix(LobbyPointCounter __instance)
        {
            __instance.playerJoinedGame = new bool[8];
            __instance.playerPlayedGame = new bool[8];
            __instance.playerAFK = new bool[8];
        }
    }

    [HarmonyPatch(typeof(LobbySkillTracker), MethodType.Constructor)]
    static class LobbySkillTrackerCtorPatch
    {
        static void Postfix(LobbySkillTracker __instance)
        {
            __instance.ratings = new Moserware.Skills.Rating[8];
        }
    }

    [HarmonyPatch(typeof(VersusControl), MethodType.Constructor)]
    static class VersusControlCtorPatch
    {
        static void Postfix(VersusControl __instance)
        {
            Debug.Log("patch VersusControl");
            __instance.winOrder = new GamePlayer[8];
            __instance.RemainingPlacements = new int[8];
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
            Debug.Log("InventoryBook cursorSpawnLocation fix");
            if (__instance.cursorSpawnLocation.Length < PlayerManager.maxPlayers)
            {
                int num2 = __instance.cursorSpawnLocation.Length;
                Array.Resize<Transform>(ref __instance.cursorSpawnLocation, PlayerManager.maxPlayers);
                for (int j = num2; j < __instance.cursorSpawnLocation.Length; j++)
                {
                    __instance.cursorSpawnLocation[j] = __instance.cursorSpawnLocation[0];
                }

            }

            //cursorSpawnLocation
        }
    }
    

    [HarmonyPatch(typeof(ControllerDisconnect), MethodType.Constructor)]
    static class ControllerDisconnectCtorPatch
    {
        static void Prefix(ControllerDisconnect __instance)
        {
            Array.Resize<XboxReconnectPrompt>(ref __instance.ConnectPrompts, PlayerManager.maxPlayers);
            __instance.orphanedReceivers = new List<InputReceiver>[]
            {
                new List<InputReceiver>(),
                new List<InputReceiver>(),
                new List<InputReceiver>(),
                new List<InputReceiver>(),
                new List<InputReceiver>(),
                new List<InputReceiver>(),
                new List<InputReceiver>()
            };
            //___showingPrompts = new bool[PlayerManager.maxPlayers];
            __instance.orphanedCharacters = new Character.Animals[PlayerManager.maxPlayers][];
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
}