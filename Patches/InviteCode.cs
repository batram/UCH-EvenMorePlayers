using HarmonyLib;
using I2.Loc;
using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace MorePlayers
{
    static class MoreCode
    {
        public const char Marker = 'M';
        public const string Stars = "*****";

        public static float lastCodeInputFocus = 0;

        public static bool IsValid(string code)
        {
            return code.Length == 5 && (code[0] == Marker || code[0] == Char.ToLower(Marker));
        }

        public static string Fudge(string code)
        {
            return Marker + code;
        }

        public static string UnFudge(string code)
        {
            return code.Substring(1);
        }

        public static void FudgeJoin(PickableNetworkButton __instance, string text)
        {
            if (!text.NullOrEmpty())
            {
                __instance.inputField.text = text;
                GameSettings.GetInstance().StartAsHost = false;
                GameSettings.GetInstance().StartLocal = false;
                Matchmaker.Instance.JoinLobby(MoreCode.UnFudge(__instance.inputField.text), true, delegate (bool success)
                {
                    if (success)
                    {
                        AnalyticEvent.JoinMatchEvent(Matchmaker.CurrentMatchmakingLobby.GetLobbyGuid(), AnalyticEvent.JoinMethod.CODE, Matchmaker.CurrentMatchmakingLobby.LobbyIsCrossplay(Application.platform));
                    }
                });
            }

        }

        public static string CleanCode(string code)
        {
            if (code.NullOrEmpty())
            {
                return null;
            }
            string text = Regex.Replace(code.ToUpper(), "[^A-Za-z]", "");
            if (!IsValid(text))
            {
                return null;
            }
            return text;
        }

        public static void CleanGUI()
        {
            var inputField_go = GameObject.Find("CodeInputField");
            var inputField = inputField_go?.GetComponent<InputField>();
            if (inputField)
            {
                inputField.characterLimit = 4;
                ((Text)inputField.placeholder).text = "ABCD";
            }
        }
    }

    [HarmonyPatch(typeof(PickableNetworkButton), nameof(PickableNetworkButton.Update))]
    static class PickableNetworkButtonUpdateCtorPatch
    {
        static bool Prefix(PickableNetworkButton __instance)
        {
            switch (__instance.job)
            {
                case PickableNetworkButton.NetworkButtonJobs.EnterLobbyCode:
                    if (__instance.inputField.isFocused)
                    {
                        MoreCode.lastCodeInputFocus = 0.15f;
                    }
                    else
                    {
                        if (MoreCode.lastCodeInputFocus > 0)
                        {
                            MoreCode.lastCodeInputFocus -= Time.deltaTime;
                        }
                    }
                    __instance.inputField.characterLimit = 5;
                    ((Text)__instance.inputField.placeholder).text = MoreCode.Fudge("ABCD");
                    if (Input.GetKeyDown(KeyCode.Return) && MoreCode.IsValid(__instance.inputField.text) && MoreCode.lastCodeInputFocus > 0)
                    {
                        UserMessageManager.Instance.UserMessage("trying to join: " + __instance.inputField.text, 2f, UserMessageManager.UserMsgPriority.lo, true);
                        MoreCode.FudgeJoin(__instance, __instance.inputField.text);
                    }
                    __instance.Show(true);
                    return false;
                case PickableNetworkButton.NetworkButtonJobs.JoinLobbyByCode:
                    __instance.Show(MoreCode.IsValid(__instance.inputField.text));
                    return false;
                case PickableNetworkButton.NetworkButtonJobs.MyLobbyCode:
                    __instance.Show(GameSettings.GetInstance().UseUnityRelay);
                    if (PickableNetworkButton.showCode && Matchmaker.CurrentMatchmakingLobby != null)
                    {
                        if (__instance.buttonText != null)
                        {
                            __instance.buttonText.text = MoreCode.Fudge(Matchmaker.CurrentMatchmakingLobby.GetLobbyCode());
                            return false;
                        }
                    }
                    else if (__instance.buttonText != null)
                    {
                        __instance.buttonText.text = MoreCode.Stars;
                        return false;
                    }
                    return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PickableNetworkButton), nameof(PickableNetworkButton.OnAccept))]
    static class PickableNetworkOnAcceptCtorPatch
    {
        static bool Prefix(PickableNetworkButton __instance)
        {
            switch (__instance.job)
            {
                case PickableNetworkButton.NetworkButtonJobs.MyLobbyCode:
                    if (Matchmaker.CurrentMatchmakingLobby != null)
                    {
                        GUIUtility.systemCopyBuffer = MoreCode.Fudge(Matchmaker.CurrentMatchmakingLobby.GetLobbyCode());
                        UserMessageManager.Instance.UserMessage(ScriptLocalization.Snapshot.ShareableCodeClipboard, 2f, UserMessageManager.UserMsgPriority.lo, true);
                    }
                    return false;
                case PickableNetworkButton.NetworkButtonJobs.PasteLobbyCode:
                    string text = MoreCode.CleanCode(GUIUtility.systemCopyBuffer);
                    MoreCode.FudgeJoin(__instance, text);
                    return false;
                case PickableNetworkButton.NetworkButtonJobs.JoinLobbyByCode:
                    MoreCode.FudgeJoin(__instance, __instance.inputField.text);
                    return false;

            }
            return true;
        }
    }

    [HarmonyPatch(typeof(TabletLobbyOptionsScreen), nameof(TabletLobbyOptionsScreen.OnClickShowToggle))]
    static class TabletLobbyOptionsScreenCtorPatch
    {
        static bool Prefix(TabletLobbyOptionsScreen __instance)
        {
            __instance.lobbyCodeShown = !__instance.lobbyCodeShown;
            if (__instance.lobbyCodeShown)
            {
                //AkSoundEngine.PostEvent("UI_UPad_Online_Lobby_Code_Show", __instance.gameObject);
                __instance.lobbyCodeText.text = MoreCode.Fudge(Matchmaker.CurrentMatchmakingLobby.GetLobbyCode());
                return false;
            }
            //kSoundEngine.PostEvent("UI_UPad_Online_Lobby_Code_Hide", __instance.gameObject);
            __instance.lobbyCodeText.text = MoreCode.Stars;
            return false;
        }
    }

    [HarmonyPatch(typeof(TabletLobbyOptionsScreen), nameof(TabletLobbyOptionsScreen.OnClickCopyLobbyCode))]
    static class TabletLobbyOptionsScreenOnClickCopyLobbyCodeCtorPatch
    {
        static bool Prefix()
        {
            QuickSaver.CopyStringToClipboard(MoreCode.Fudge(Matchmaker.CurrentMatchmakingLobby.GetLobbyCode()));
            UserMessageManager.Instance.UserMessage(ScriptLocalization.Snapshot.ShareableCodeClipboard, 2f, UserMessageManager.UserMsgPriority.lo, true);
            return false;
        }
    }

    [HarmonyPatch(typeof(TabletLobbyOptionsScreen), nameof(TabletLobbyOptionsScreen.Awake))]
    static class TabletLobbyOptionsScreenAwakeCtorPatch
    {
        static void Prefix(TabletLobbyOptionsScreen __instance)
        {
            __instance.lobbyCodeText.text = MoreCode.Stars;
        }
    }
}