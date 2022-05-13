using HarmonyLib;
using System;
using UnityEngine;
using UnityEngine.Networking;

namespace MorePlayers
{
    [HarmonyPatch(typeof(LobbyPlayer), nameof(LobbyPlayer.CmdAssignCharacter))]
    static class LobbyPlayerCtorPatch
    {
        static void Prefix(LobbyPlayer __instance, ref uint characterNetID)
        {
            GameObject gameObject = NetworkServer.FindLocalObject(new NetworkInstanceId(characterNetID));
            if (gameObject != null)
            {
                Character component = gameObject.GetComponent<Character>();

                if (component != null)
                {
                    Character car = UnityEngine.Object.Instantiate<Character>(component, component.transform.position, Quaternion.identity);
                    car.GetComponent<NetworkIdentity>().ForceSceneId(0);
                    Debug.Log("car picked: " + car.picked);
                    car.picked = false;
                    foreach (BoxCollider2D c in car.gameObject.GetComponents<BoxCollider2D>())
                    {
                        c.enabled = true;
                    }
                    NetworkServer.Spawn(car.gameObject);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
    static class CharacterCtorPatch
    {
        static void Prefix(Character __instance)
        {
            Debug.Log("char awake: " + __instance.gameObject.name);
            foreach (BoxCollider2D c in __instance.gameObject.GetComponents<BoxCollider2D>())
            {
                c.enabled = true;
            }
        }
    }

    [HarmonyPatch(typeof(LevelSelectController), nameof(LevelSelectController.SetupLobbyAfterWait))]
    static class LevelSelectControllerSetupLobbyAfterWaitCtorPatch
    {
        static void Prefix(LevelSelectController __instance)
        {
            if (!LobbyManager.instance)
            {
                return;
            }
            foreach (LobbyPlayer lobbyPlayer in LobbyManager.instance.lobbySlots)
            {
                if (!(lobbyPlayer == null))
                {
                    if (!lobbyPlayer.IsLocalPlayer)
                    {
                        lobbyPlayer.FindLobbyObjects();
                    }

                    try
                    {
                        lobbyPlayer.UnpickCharacter();
                    }
                    catch (Exception e)
                    {
                        Debug.Log("unpick ex: " + e);
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(OutfitManager), nameof(OutfitManager.RebuildDatabase))]
    static class OutfitManagerRebuildDatabaseTakenCtorPatch
    {
        static bool Prefix()
        {
            Debug.Log("OutfitManager.RebuildDatabase: Skip broken function for now");
            return false;
        }
    }

    [HarmonyPatch(typeof(LevelSelectController), nameof(LevelSelectController.IsCharacterTaken))]
    static class LevelSelectControllerIsCharacterTakenCtorPatch
    {
        static void Postfix(ref bool __result, LevelSelectController __instance)
        {
            Debug.Log("LevelSelectController.IsCharacterTaken false");
            __result = false;
        }
    }
}