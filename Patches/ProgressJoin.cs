using HarmonyLib;
using I2.Loc;
using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MorePlayers
{
    static class ProgessJoin
    {

        [HarmonyPatch(typeof(PickableNetworkButton), nameof(PickableNetworkButton.OnAccept))]
        static class PickableNetworkOnAcceptCtorPatch
        {
            static bool Prefix(PickableNetworkButton __instance)
            {
                switch (__instance.job)
                {
                    case PickableNetworkButton.NetworkButtonJobs.SearchResult:
                        if (__instance.TryingToConnect)
                        {
                            return false;
                        }
                        __instance.JoinGame();
                        return false;
                }
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(UnityMatchmaker), nameof(UnityMatchmaker.CheckHostConnectivity))]
    static class UnityMatchmakerCheckHostConnectivityCtorPatch
    {
        static bool Prefix(UnityMatchmaker __instance)
        {
            if (__instance.CurrentLobby != null)
            {
                int matchProgress = __instance.CurrentLobby.GetMatchProgress();
                if (matchProgress != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    
    [HarmonyPatch(typeof(SteamMatchmakingLobby), nameof(SteamMatchmakingLobby.GetMatchProgress))]
    [HarmonyPatch(typeof(GamesparksMatchmakingLobby), nameof(GamesparksMatchmakingLobby.GetMatchProgress))]
    static class GamesparksMatchmakingLobbyGetMatchProgressCtorPatch
    {
        static bool Prefix(ref int __result)
        {
            __result = 0;
            return false;
        }
    }

    [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.DisconnectBrokenClients))]
    static class LobbyManagerDisconnectBrokenClientsCtorPatch
    {
        static bool Prefix()
        {
            return false;
        }
    }


    
    [HarmonyPatch(typeof(NetworkLobbyManager), nameof(NetworkLobbyManager.OnServerConnect))]
    static class NetworkLobbyManagerOnServerConnectCtorPatch
    {
        static bool Prefix(NetworkLobbyManager __instance)
        {
            if (SceneManager.GetSceneAt(0).name != __instance.m_LobbyScene)
            {
                Debug.Log(__instance.m_LobbyScene + " fake lobby " + SceneManager.GetSceneAt(0).name);
                __instance.m_LobbyScene = SceneManager.GetSceneAt(0).name;
            }
            return true;
        }
    }

    /*
    [HarmonyPatch(typeof(LobbyManagerManager), nameof(LobbyManagerManager.AbortGameInProgress))]
    static class LobbyManagerManagerAbortGameInProgressCtorPatch
    {
        static bool Prefix(LobbyManagerManager __instance)
        {
            return false;
        }
    }
    */
    [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.OnLobbyClientSceneChanged))]
    static class LobbyManagerOnLobbyClientSceneChangedCtorPatch
    {
        static bool Prefix(LobbyManagerManager __instance)
        {
            return false;
        }
    }
}