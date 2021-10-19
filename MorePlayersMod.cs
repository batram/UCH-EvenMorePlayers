using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace MorePlayers
{
    [BepInPlugin("notfood.MorePlayers", "MorePlayers", "1.0.0.0")]
    public class MorePlayersMod : BaseUnityPlugin
    {
        void Awake()
        {
            new Harmony("notfood.UltimateBuilder").PatchAll();

            Debug.Log("[MorePlayersMod] started.");
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

    [HarmonyPatch(typeof(GraphScoreBoard), MethodType.Constructor)]
    static class GraphScoreBoardCtorPatch
    {
        static void Postfix(GraphScoreBoard __instance)
        {
            __instance.ScorePositions = new RectTransform[8];
        }
    }

    [HarmonyPatch(typeof(LevelSelectController), MethodType.Constructor)]
    static class LevelSelectControllerCtorPatch
    {
        static void Postfix(LevelSelectController __instance)
        {
            __instance.JoinedPlayers = new LobbyPlayer[8];
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
            __instance.winOrder = new GamePlayer[8];
            __instance.RemainingPlacements = new int[8];
        }
    }
}