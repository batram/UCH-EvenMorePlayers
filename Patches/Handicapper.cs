using GameEvent;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MorePlayers
{
    static class HandiShuffle
    {
        public static void ShowLine(HandicapLine line, bool show)
        {
            line.ScorelineStretcher.SetActive(show);
            line.AnimalName.gameObject.SetActive(show);
            line.HandicapNumber.gameObject.SetActive(show);
        }

        public static void reCap()
        {
            if (!MorePlayersMod.shuffleScoreBalancer.Value)
                return;

            GameObject handicapper = GameObject.Find("Handicapper");
            handicap handi = handicapper?.GetComponent<handicap>();
            HandiShuffle.pushHandicaps(handi, -1);
        }


        public static void pushHandicaps(handicap handicap, int networkPlayerNumber)
        {
            if (!MorePlayersMod.shuffleScoreBalancer.Value)
                return;

            HandicapLine slot0 = handicap.HandicapLineSlots[0].GetComponentInChildren<HandicapLine>();
            HandicapLine slot1 = handicap.HandicapLineSlots[1].GetComponentInChildren<HandicapLine>();
            HandicapLine slot2 = handicap.HandicapLineSlots[2].GetComponentInChildren<HandicapLine>();
            HandicapLine slot3 = handicap.HandicapLineSlots[3].GetComponentInChildren<HandicapLine>();

            HandicapLine[] lines = { slot0, slot1, slot2, slot3 };
            int[] playerNumbers = { slot0.PlayerNetworkNumber, slot1.PlayerNetworkNumber, slot2.PlayerNetworkNumber, slot3.PlayerNetworkNumber };
            playerNumbers = playerNumbers.Where(c => {
                LobbyPlayer player = LobbyManager.instance.GetLobbyPlayer(c);
                return (c != networkPlayerNumber && c != -1 && player?.PickedAnimal != Character.Animals.NONE); 
            }).ToArray();

            if(playerNumbers.Length < 4)
            {
                //add all players with non 100 handicap to list when list is empty
                foreach (LobbyPlayer player in LobbyManager.instance.GetLobbyPlayers()) { 
                    if(player?.PickedAnimal != Character.Animals.NONE && player.handicap != 100)
                    {
                        if (player.networkNumber != networkPlayerNumber && !playerNumbers.Contains(player.networkNumber))
                            playerNumbers = playerNumbers.Append(player.networkNumber).ToArray();
                    }
                }
            }

            if(playerNumbers.Length < 4)
            {
                int[] fillPlayerNumbers = { -1, -1, -1, -1 };
                for (int x = 0; x < playerNumbers.Length; x++)
                {
                    fillPlayerNumbers[x] = playerNumbers[x];
                }
                playerNumbers = fillPlayerNumbers;
            }

            Dictionary<int, float> ogNums = new Dictionary<int, float>();

            foreach (HandicapLine line in lines)
            {
                if (!ogNums.ContainsKey(line.PlayerNetworkNumber) && line.PlayerNetworkNumber != -1)
                {
                    ogNums.Add(line.PlayerNetworkNumber, line.currentHandicapFloat);
                }
                line.PlayerNetworkNumber = -1;
            }

            int[] newPlayerNumbers = { networkPlayerNumber, playerNumbers[0], playerNumbers[1], playerNumbers[2] };

            if(networkPlayerNumber == -1)
            {
                newPlayerNumbers = playerNumbers;
            }

            int i = 0;
            foreach (int playerNum in newPlayerNumbers)
            {
                bool emptySlot = playerNum == -1;

                lines[i].PlayerNetworkNumber = playerNum;
                ShowLine(lines[i], !emptySlot);

                if (!emptySlot)
                {
                    bool skipTransition = playerNum != networkPlayerNumber || networkPlayerNumber == -1;
                    updateHandicapLine(lines[i], playerNum, skipTransition, ogNums);
                }
                i++;
            }
        }

        private static void updateHandicapLine(HandicapLine line, int networkPlayerNumber, bool skipTransition, Dictionary<int, float> ogNums)
        {
            LobbyPlayer player = LobbyManager.instance.GetLobbyPlayer(networkPlayerNumber);
            if (!player)
            {
                line.PlayerNetworkNumber = -1;
                ShowLine(line, false);
                return;
            }

            line.AnimalName.text = player.playerName;

            line.targetHandicap = player.handicap;
            line.HandicapNumber.text = line.targetHandicap.ToString() + "%";
            if (skipTransition)
            {
                // skip transition animation if just switching lines
                line.currentHandicapFloat = (float)line.targetHandicap / 100f;
                line.ScorelineStretcher.transform.localScale = new Vector3(line.initialScale.x * line.currentHandicapFloat, line.initialScale.y, line.initialScale.y);
            }
            else
            {
                if (ogNums.ContainsKey(networkPlayerNumber))
                {
                    line.currentHandicapFloat = ogNums.GetValueSafe(networkPlayerNumber);
                }
            }

        }
    }

    [HarmonyPatch(typeof(handicap), nameof(handicap.handleEvent))]
    static class HandicapHandleEventCtorPatch
    {
        static void Postfix(handicap __instance, GameEvent.GameEvent e)
        {
            if (e.GetType() == typeof(NetworkMessageReceivedEvent))
            {
                //Set on Client
                NetworkMessageReceivedEvent networkMessageReceivedEvent = e as NetworkMessageReceivedEvent;
                short msgType = networkMessageReceivedEvent.Message.msgType;

                if (msgType == NetMsgTypes.PlayerHandicapSet)
                {
                    MsgPlayerHandicapSet msgPlayerHandicapSet = (MsgPlayerHandicapSet)networkMessageReceivedEvent.ReadMessage;
                    HandiShuffle.pushHandicaps(__instance, msgPlayerHandicapSet.NetworkPlayerNumber);
                }
                if (msgType == NetMsgTypes.SetCustomPortalInfo || msgType == NetMsgTypes.CommunicateCharacterOutfits)
                {
                    HandiShuffle.pushHandicaps(__instance, -1);
                }
            }
            else
            {
                HandiShuffle.pushHandicaps(__instance, -1);
            }
        } 
    }

    [HarmonyPatch(typeof(handicap), nameof(handicap.Start))]
    static class HandicapStartCtorPatch
    {
        static void Postfix(handicap __instance)
        {
            if (!MorePlayersMod.shuffleScoreBalancer.Value)
                return;

            GameObject title = GameObject.Find("Handicapper/Canvas/Title");
            Text titleText = title.GetComponent<Text>();
            titleText.text = "modded Score Balancer";

            foreach(Transform t in __instance.HandicapLineSlots)
            {
                HandicapLine line = t.GetComponentInChildren<HandicapLine>();

                if (!line.currentlyShown || line.targetHandicap == -999)
                {
                    line.PlayerNetworkNumber = -1;
                    HandiShuffle.ShowLine(line, false);
                }
            }

            GameEventManager.ChangeListener<CharacterPickedEvent>(__instance, true);
            GameEventManager.ChangeListener<LobbyPlayerRemovedEvent>(__instance, true);
            GameEventManager.ChangeListener<LobbyPlayerCreatedEvent>(__instance, true);
            GameEventManager.ChangeListener<LocalPlayerAddedEvent>(__instance, true);
            GameEventManager.ChangeListener<CharacterVoteEvent>(__instance, true);
            GameEventManager.ChangeListener<GameEndEvent>(__instance, true);
        }
    }

    [HarmonyPatch(typeof(HandicapLine), nameof(HandicapLine.SetName))]
    static class HandicapLineSetNameCtorPatch
    {
        static bool Prefix(HandicapLine __instance, Character.Animals animal, bool altSkin)
        {
            if (!MorePlayersMod.shuffleScoreBalancer.Value)
                return true;

            LobbyPlayer lobbyPlayer = LobbyManager.instance.GetLobbyPlayer(__instance.PlayerNetworkNumber);
            __instance.AnimalName.text = lobbyPlayer.playerName;
            return false;
        }
    }

    [HarmonyPatch(typeof(LobbyPlayer), nameof(LobbyPlayer.CmdSetPlayerHandicap))]
    static class LobbyPlayerHandleEventCtorPatch
    {
        static void Postfix(LobbyPlayer __instance, int newHandicap)
        {
            HandiShuffle.reCap();
        }
    }

    [HarmonyPatch(typeof(LobbyPlayer), nameof(LobbyPlayer.DoCharacterPickedEvent))]
    static class LobbyPlayerDoCharacterPickedEventCtorPatch4Handicap
    {
        static void Postfix(ref bool clearOutfit)
        {
            HandiShuffle.reCap();
        }
    }

    [HarmonyPatch(typeof(LobbyPlayer), nameof(LobbyPlayer.RpcRequestPickResponse))]
    static class LobbyPlayerRpcRequestPickResponseCtorPatch4Handicap
    {
        static void Postfix()
        {
            HandiShuffle.reCap();
        }
    }
}