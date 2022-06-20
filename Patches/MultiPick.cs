using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace MorePlayers
{
    static class MultiPick
    {

        public const int multiMagicNumber = 10000;

        public static Character SpawnCharacter(Character to_clone, Vector3 position)
        {
            Character new_character = UnityEngine.Object.Instantiate<Character>(to_clone, position, Quaternion.identity);
            new_character.GetComponent<NetworkIdentity>().ForceSceneId(0);
            UnityEngine.Object.Destroy(new_character.GetComponent<OGProtection>());

            new_character.picked = true;
            new_character.FindPlayerOnSpawn = true;
            new_character.gameObject.transform.parent = null;

            //Remove all original ArtMatches, new ones are added during pick event (breaks outfit selection)
            var arties = new_character.GetComponentsInChildren<ArtMatcher>();
            for (var i = 0; i < arties.Length; i++)
            {
                UnityEngine.Object.Destroy(arties[i].gameObject);
            }

            NetworkServer.Spawn(new_character.gameObject);

            return new_character;
        }
    }

    public class OGProtection : MonoBehaviour
    {

    }

    [HarmonyPatch(typeof(LevelSelectController), nameof(LevelSelectController.RpcResetCharacter))]
    static class LevelSelectControllerRpcResetCharacterCtorPatch
    {
        static void Prefix(LevelSelectController __instance, GameObject characterObj)
        {
            Character c = characterObj.GetComponent<Character>();
            if (c)
            {
                __instance.MainCamera.RemoveTarget(c);
            }
        }

        static void Postfix(GameObject characterObj)
        {
            NetworkServer.Destroy(characterObj);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
    static class CharacterCtorPatch
    {
        static void Prefix(Character __instance)
        {
            //Turn on all colliders, fix for collision modifier
            foreach (BoxCollider2D c in __instance.gameObject.GetComponents<BoxCollider2D>())
            {
                c.enabled = true;
            }

            //RefreshScale: fix for collision modifier
            __instance.RefreshScale();
        }
    }

    [HarmonyPatch(typeof(OutfitManager), nameof(OutfitManager.RebuildDatabase))]
    static class OutfitManagerRebuildDatabaseTakenCtorPatch
    {
        static bool Prefix(OutfitManager __instance)
        {
            Debug.Log("OutfitManager.RebuildDatabase: fix");

            __instance.characterOutfitsUnlocked.Clear();
            __instance.characterOutfitsAll.Clear();
            __instance.characterArtMatchers = UnityEngine.Object.FindObjectsOfType<ArtMatcher>();
            foreach (ArtMatcher artMatcher in __instance.characterArtMatchers)
            {
                if (artMatcher.character.name.Contains("Clone") && !artMatcher.character.name.Contains("moep") && artMatcher.character.GetComponent<NetworkIdentity>())
                {
                    artMatcher.character.name += " moep " + artMatcher.character.GetComponent<NetworkIdentity>().netId;
                }

                if (!__instance.characterOutfitsUnlocked.ContainsKey(artMatcher.character))
                {
                    __instance.characterOutfitsUnlocked.Add(artMatcher.character, new List<Outfit>[Outfit.NumOutfitTypes]);
                }
                if (!__instance.characterOutfitsAll.ContainsKey(artMatcher.character))
                {
                    __instance.characterOutfitsAll.Add(artMatcher.character, new List<Outfit>[Outfit.NumOutfitTypes]);
                }

                for (int j = 0; j < Outfit.NumOutfitTypes; j++)
                {
                    __instance.characterOutfitsUnlocked[artMatcher.character][j] = new List<Outfit>();
                    __instance.characterOutfitsAll[artMatcher.character][j] = new List<Outfit>();
                }

                foreach (Outfit outfit in artMatcher.outfits)
                {
                    if (outfit.outfitType != Outfit.OutfitType.FollowOutfit && outfit.outfitType != Outfit.OutfitType.Zombie)
                    {
                        __instance.characterOutfitsAll[artMatcher.character][(int)outfit.outfitType].Add(outfit);
                        bool flag = outfit.Unlocked;
                        if (!flag && artMatcher.GetDefaultForcedOutfit(outfit.outfitType) == outfit)
                        {
                            outfit.TempUnlocked = true;
                            flag = true;
                        }
                        if (flag)
                        {
                            __instance.characterOutfitsUnlocked[artMatcher.character][(int)outfit.outfitType].Add(outfit);
                        }
                    }
                }

                if (artMatcher.character && artMatcher.character.associatedLobbyPlayer)
                {
                    artMatcher.character.SetOutfitsFromArray(artMatcher.character.associatedLobbyPlayer.characterOutfitsList);
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(OutfitController), nameof(OutfitController.Show))]
    static class OutfitControllerupdateImagesCtorPatch
    {
        static void Prefix(OutfitController __instance)
        {
            __instance.OutfitManager.RebuildDatabase();
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetOutfitsAsArray))]
    static class GetOutfitsAsArrayCtorPatch
    {
        static void Postfix(Character __instance, ref int[] __result)
        {
            var netid = (int)__instance.GetComponent<NetworkIdentity>()?.netId.Value;
            if (netid != null && netid != 0)
            {
                Array.Resize<int>(ref __result, __result.Length + 1);
                __result[__result.Length - 1] = netid;
            }
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.SetOutfitsFromArray), new Type[] { typeof(SyncListInt) })]
    static class SetOutfitsFromArraySyncListIntCtorPatch
    {
        static bool Prefix(Character __instance, SyncListInt outfitsSyncList)
        {
            Debug.Log("Character.SetOutfitsFromArray " + outfitsSyncList.Count);
            int[] array = new int[outfitsSyncList.Count == 7 ? 7 : 6];
            for (int i = 0; i < array.Length; i++)
            {
                if (outfitsSyncList.Count > i)
                {
                    array[i] = outfitsSyncList[i];
                }
                else
                {
                    array[i] = -1;
                }
            }
            __instance.SetOutfitsFromArray(array);
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.SetOutfitsFromArray), new Type[] { typeof(int[]) })]
    static class SetOutfitsFromArrayCtorPatch
    {
        static bool Prefix(Character __instance, ref int[] outfitsArray)
        {
            if (outfitsArray.Length == 7)
            {
                var character_go = ClientScene.FindLocalObject(new NetworkInstanceId((uint)outfitsArray[6]));
                var character = character_go?.GetComponent<Character>();
                Array.Resize<int>(ref outfitsArray, outfitsArray.Length - 1);
                if (character)
                {
                    character.SetOutfitsFromArray(outfitsArray);
                    return false;
                }
            }
            return true;
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

    [HarmonyPatch(typeof(Modifiers), nameof(Modifiers.OnModifiersDynamicChange))]
    static class ModifiersCtorPatch
    {
        static bool Prefix(Modifiers __instance)
        {
            Debug.Log("Modifiers.OnModifiersDynamicChange: fix size modifier " + __instance.CharacterScale + " " + Modifiers.GetInstance().CharacterScaleAudioStateString);
            if (LobbyManager.instance != null && LobbyManager.instance.CurrentLevelSelectController)
            {
                __instance.modsApplied = __instance.modsPreview;
                LobbyManager.instance.CurrentLevelSelectController.RefreshCharacterPosition();
            }
            if (LobbyManager.instance != null && (LobbyManager.instance.CurrentGameController != null || LobbyManager.instance.CurrentLevelSelectController != null))
            {
                Character[] array = UnityEngine.Object.FindObjectsOfType<Character>();
                for (int i = 0; i < array.Length; i++)
                {
                    array[i].RefreshScale();
                }
                Time.timeScale = __instance.GameSpeed;
            }
            AkSoundEngine.PostEvent(__instance.RateOfFireAudioEventStrings[__instance.RateOfFireMode], LobbyManager.instance.gameObject);

            return false;
        }
    }

    [HarmonyPatch(typeof(LevelSelectController), nameof(LevelSelectController.setupController))]
    static class LevelSelectControllerSetupControllerCtorPatch
    {
        static bool Prefix(LevelSelectController __instance, LobbyPlayer lobbyPl)
        {
            Player localPlayer = lobbyPl.LocalPlayer;
            Character.Animals[] associatedCharacters = localPlayer.UseController.GetAssociatedCharacters();
            if (!localPlayer.UseController.ControlsPlayer(localPlayer.Number))
            {
                localPlayer.UseController.AddPlayer(localPlayer.Number);
            }
            for (int i = associatedCharacters.Length - 1; i >= 0; i--)
            {
                if (associatedCharacters[i] != Character.Animals.NONE && lobbyPl.PickedAnimal == associatedCharacters[i])
                {
                    Debug.Log("Setting up " + associatedCharacters[i].ToString());
                    __instance.MainCamera.SetFrameSizes(__instance.CameraHeight);
                    lobbyPl.PlayerStatus = LobbyPlayer.Status.CHARACTER;

                    Character[] chars = GameObject.FindObjectsOfType<Character>();
                    foreach (Character character in chars)
                    {
                        if (associatedCharacters[i] == character.CharacterSprite)
                        {
                            if (!character.picked)
                            {
                                Debug.Log("Requesting AssociatedCharacter " + associatedCharacters[i].ToString());

                                LobbyCursor lobbyCursor = (LobbyCursor)localPlayer.AssociatedLobbyPlayer.CursorInstance;
                                if (lobbyCursor != null)
                                {
                                    localPlayer.AssociatedLobbyPlayer.requestedCharacterInstance = null;
                                    lobbyCursor.UseCamera = __instance.MainCamera.GetComponent<Camera>();
                                    localPlayer.UseController.AddReceiver(lobbyCursor);
                                }

                                localPlayer.AssociatedLobbyPlayer.RequestPickCharacter(character);
                            }
                        }
                    }
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(LobbyPlayer), nameof(LobbyPlayer.CmdRequestPickCharacter))]
    static class LobbyPlayerCmdRequestPickCharacterCtorPatch
    {
        static bool Prefix(LobbyPlayer __instance, NetworkInstanceId characterInstanceId, Character.Animals animal)
        {
            var character_go = NetworkServer.FindLocalObject(characterInstanceId);
            if (character_go && character_go.GetComponent<Character>())
            {
                Character requested_character = character_go.GetComponent<Character>();

                if (requested_character.picked || requested_character.gameObject.GetComponent<OGProtection>() != null)
                {
                    Vector3 spawn_position = requested_character.transform.position;
                    if (__instance.playerStatus == LobbyPlayer.Status.CHARACTER && (!GameState.GetInstance().currentSnapshotInfo.snapshotName.NullOrEmpty() || GameState.GetInstance().lastLevelPlayed == GameState.GetLevelSceneName(GameState.LevelName.BLANKLEVEL)))
                    {
                        spawn_position = LobbyManager.instance.CurrentLevelSelectController.UndergroundCharacterPosition[__instance.networkNumber - 1].position;
                    }

                    Character nspawn_char = MultiPick.SpawnCharacter(requested_character, spawn_position);
                    uint new_id = nspawn_char.gameObject.GetComponent<NetworkIdentity>().netId.Value;

                    __instance.CallCmdAssignCharacter(new_id, __instance.networkNumber, __instance.localNumber, false);
                    __instance.CallRpcRequestPickResponse((int)(new_id * MultiPick.multiMagicNumber) + __instance.networkNumber, false);
                }
                else
                {
                    __instance.CallCmdAssignCharacter(characterInstanceId.Value, __instance.networkNumber, __instance.localNumber, false);
                    __instance.CallRpcRequestPickResponse(__instance.networkNumber, true);
                }
            }
            else
            {
                __instance.CallRpcRequestPickResponse(__instance.networkNumber, false);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(LobbyPlayer), nameof(LobbyPlayer.RpcRequestPickResponse))]
    static class LobbyPlayerRpcRequestPickResponseCtorPatch
    {
        static bool Prefix(LobbyPlayer __instance, ref int playerNetworkNumber, ref bool response)
        {
            Debug.Log("LobbyPlayer.RpcRequestPickResponse Prefix  " + playerNetworkNumber + " response " + response);

            if (!response && playerNetworkNumber > MultiPick.multiMagicNumber)
            {
                var new_id = playerNetworkNumber / MultiPick.multiMagicNumber;
                playerNetworkNumber %= MultiPick.multiMagicNumber;

                var character_go = ClientScene.FindLocalObject(new NetworkInstanceId((uint)new_id));
                if (character_go)
                {
                    var car = character_go.GetComponent<Character>();
                    Debug.Log("Got reassign Character comp " + car);
                    if (car)
                    {
                        __instance.requestedCharacterInstance = car;
                        response = true;
                    }
                }

            }
            return true;
        }

        static void Postfix(LobbyPlayer __instance, ref int playerNetworkNumber, ref bool response)
        {
            Debug.Log("LobbyPlayer.RpcRequestPickResponse Postfix " + playerNetworkNumber + " response " + response);
        }
    }

    [HarmonyPatch(typeof(LevelSelectController), nameof(LevelSelectController.Start))]
    static class LevelSelectControllerStartCtorPatch
    {
        static void Prefix(LevelSelectController __instance)
        {
            Character[] chars = GameObject.FindObjectsOfType<Character>();
            Debug.Log("Found " + chars.Length + " chars");
            foreach (Character ca in chars)
            {
                ca.gameObject.AddComponent<OGProtection>();
            }
        }
    }

    [HarmonyPatch(typeof(LevelSelectController), nameof(LevelSelectController.SetupLobbyAfterWait))]
    static class LevelSelectControllerSetupLobbyAfterWaitCtorPatch
    {
        static void Prefix(LevelSelectController __instance)
        {
            Character[] chars = GameObject.FindObjectsOfType<Character>();
            var num = 0;
            foreach (Character ca in chars)
            {
                if (ca.Picked && !ca.Sitting)
                {
                    __instance.MainCamera.AddTarget(ca);
                    num++;
                    ca.SetLobbyCollider(true);
                }
            }
            if (num > 0)
            {
                __instance.MainCamera.SetFrameSizes(__instance.CameraHeight);
            }
        }
    }

    [HarmonyPatch(typeof(LobbyPlayer), nameof(LobbyPlayer.DoCharacterPickedEvent))]
    static class LobbyPlayerDoCharacterPickedEventCtorPatch
    {
        static void Prefix(ref bool clearOutfit)
        {
            Debug.Log("DoCharacterPickedEvent " + clearOutfit);
            clearOutfit = false;
        }
    }

    [HarmonyPatch(typeof(LevelSelectController), nameof(LevelSelectController.setupLobby))]
    static class LevelSelectControllerSetupLobbyCtorPatch
    {
        static void Postfix(LevelSelectController __instance)
        {
            if (!GameState.GetInstance().currentSnapshotInfo.snapshotName.NullOrEmpty() || GameState.GetInstance().lastLevelPlayed == GameState.GetLevelSceneName(GameState.LevelName.BLANKLEVEL))
            {
                foreach (LobbyStartPoint lobbyStartPoint in __instance.StartingPoints)
                {
                    Character componentInChildren = lobbyStartPoint.GetComponentInChildren<Character>();
                    componentInChildren.PositionCharacter(lobbyStartPoint.transform.position, true);
                }
            }
        }
    }

    [HarmonyPatch(typeof(GraphScoreBoard), nameof(GraphScoreBoard.MarkPlayerDisconnected))]
    static class GraphScoreBoardMarkPlayerDisconnectedCtorPatch
    {
        static bool Prefix()
        {
            //Disconnect Animal Enum based, reimplement in VersusControl.handleEvent 
            return false;
        }
    }

    [HarmonyPatch(typeof(VersusControl), nameof(VersusControl.handleEvent))]
    static class VersusControlHandleEventCtorPatch
    {
        static void Prefix(VersusControl __instance, GameEvent.GameEvent e)
        {
            Type type = e.GetType();
            if (type == typeof(GameEvent.GamePlayerRemovedEvent))
            {
                GameEvent.GamePlayerRemovedEvent removedEvent = e as GameEvent.GamePlayerRemovedEvent;
                var relation = __instance.graphScoreBoardInstance.scorelineRelation;
                if (relation.ContainsKey(removedEvent.PlayerNetworkNumber))
                {
                    relation[removedEvent.PlayerNetworkNumber].SetDisconnected(true);
                }
            }
        }
    }
}