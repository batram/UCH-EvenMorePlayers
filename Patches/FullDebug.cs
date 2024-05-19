using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace MorePlayers
{
    [HarmonyPatch(typeof(LevelSelectController), nameof(LevelSelectController.SetupLobbyAfterWait))]
    [HarmonyPatch(typeof(StartGameState), nameof(StartGameState.Awake))]
    static class StartGameStateCtorPatch
    {
        static void Postfix()
        {
            if (MorePlayersMod.fullDebug.Value)
            {
                LogFilter.currentLogLevel = 0;
                Debug.logger.logEnabled = true;

                Debug.Log("StartGameStateCtorPatch");
                Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.Full);
                Application.SetStackTraceLogType(LogType.Assert, StackTraceLogType.Full);
                Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.Full);
                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.Full);
                Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.Full);

            }
        }
    }
}