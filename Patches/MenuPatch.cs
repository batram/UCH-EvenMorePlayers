using HarmonyLib;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace MorePlayers
{
    static class MenuPatch
    {
        static public void PatchMenu()
        {
            var harmony = new Harmony("notfood.MorePlayers.MenuPatch");
            var original = typeof(TabletMainMenuHome).GetMethod(nameof(TabletMainMenuHome.Initialize));
            var postfix = typeof(TabletMainMenuHomeCtorPatch).GetMethod(nameof(TabletMainMenuHomeCtorPatch.Postfix));
            harmony.Patch(original, postfix: new HarmonyMethod(postfix));

            var update_original = typeof(TabletMainMenuHome).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            var update_postfix = typeof(TabletMainMenuHomeScoochButtonsCtorPatch).GetMethod("Postfix");
            harmony.Patch(update_original, postfix: new HarmonyMethod(update_postfix));

            var accept_original = typeof(TabletButton).GetMethod(nameof(TabletButton.OnAccept));
            var accept_prefix = typeof(TabletButtonOnAcceptCtorPatch).GetMethod(nameof(TabletButtonOnAcceptCtorPatch.Prefix));
            harmony.Patch(accept_original, prefix: new HarmonyMethod(accept_prefix));

            var onlinestate_original = typeof(TabletMainMenuOnlineIndicator).GetMethod("SetPlayOnlineButtonState", BindingFlags.NonPublic | BindingFlags.Instance);
            var onlinestate_prefix = typeof(TabletMainMenuOnlineIndicatorCtorPatch).GetMethod("Prefix");
            harmony.Patch(onlinestate_original, prefix: new HarmonyMethod(onlinestate_prefix));

            var version_original = typeof(GameSettings).GetMethod("get_VersionNumber");
            var version_prefix = typeof(GameSettingsVersionCtorPatch).GetMethod("Prefix");
            harmony.Patch(version_original, prefix: new HarmonyMethod(version_prefix));
        }
    }

    //TabletButton::OnAccept(PickCursor pickCursor)
    static class TabletButtonOnAcceptCtorPatch
    {
        static public void Prefix(TabletButton __instance)
        {
            Debug.Log("Pressed da button: " + __instance.gameObject.name);
            if (__instance.gameObject.name == "Play More")
            {
                if (!GameSettings.GetInstance().versionNumber.Contains(MorePlayersMod.mod_version_full))
                {
                    GameSettings.GetInstance().versionNumber = GameSettings.GetInstance().VersionNumber + MorePlayersMod.mod_version_full;
                    if (PlayerManager.maxPlayers != MorePlayersMod.newPlayerLimit)
                    {
                        PlayerManager.maxPlayers = MorePlayersMod.newPlayerLimit;
                        new Harmony("notfood.MorePlayers.PlayerNumPatch").PatchAll();
                    }
                }
            }
            else if (__instance.gameObject.name == "Play Online")
            {
                GameSettings.GetInstance().versionNumber = MorePlayersMod.og_version;
                PlayerManager.maxPlayers = 4;
                Harmony.UnpatchID("notfood.MorePlayers.PlayerNumPatch");
                MoreCode.CleanGUI();
            }
        }
    }

    static class TabletMainMenuHomeCtorPatch
    {
        static public void Postfix(ChallengeScoreboard __instance)
        {
            __instance.players = new ChallengeScoreboard.ChallengePlayer[PlayerManager.maxPlayers];
            GameObject more_button = GameObject.Find("main Buttons/Play More");
            if (more_button == null)
            {
                //Adjust local button
                GameObject play_local = GameObject.Find("main Buttons/Play");
                var local_label = play_local.transform.Find("Text Label").GetComponent<TabletTextLabel>();
                local_label.text = "Local";
                local_label.transform.position -= new Vector3(0.65f, 0f, 0f);
                var local_image = play_local.transform.Find("Image");
                local_image.transform.localScale = new Vector3(0.7073f, 0.7073f, 1f);
                local_image.transform.position -= new Vector3(0.04f, 0f, 0f);

                //Adjust online button
                GameObject play_online = GameObject.Find("main Buttons/Play Online");
                var online_label = play_online.transform.Find("Text Label").GetComponent<TabletTextLabel>();
                online_label.text = "Online";
                online_label.transform.position -= new Vector3(0.7f, 0f, 0f);
                var online_image = play_online.transform.Find("Image");
                online_image.transform.localScale = new Vector3(0.8073f, 0.8073f, 1f);
                online_image.transform.position -= new Vector3(0.11f, 0f, 0f);

                //Add more button
                more_button = UnityEngine.Object.Instantiate<GameObject>(play_online);
                more_button.name = "Play More";
                more_button.transform.SetParent(play_online.transform.parent);
                more_button.transform.localScale = new Vector3(1f, 1f, 1f);
                var more_label = more_button.transform.Find("Text Label").GetComponent<TabletTextLabel>();
                more_label.text = "More";
                more_label.transform.position -= new Vector3(0.6655f, 0f, 0f);

                var more_image = more_button.transform.Find("Image");
                more_image.transform.localScale = new Vector3(-0.5073f, 0.5073f, 1f);
                more_image.transform.position += new Vector3(-0.06f, 0.5073f, 0f);
                var more_image1 = Object.Instantiate<GameObject>(more_image.gameObject, more_image.transform.parent);
                more_image1.name = "Image1";
                more_image1.transform.position = more_image.transform.position;
                more_image1.transform.position -= new Vector3(0.6f, 1.2f, 0f);
                more_image1.transform.localScale = new Vector3(-0.5073f, 0.5073f, 1f);

                var more_image2 = Object.Instantiate<GameObject>(more_image.gameObject, more_image.transform.parent);
                more_image2.name = "Image2";
                more_image2.transform.position = more_image.transform.position;
                more_image2.transform.position -= new Vector3(-0.6f, 1.2f, 0f);
                more_image2.transform.localScale = new Vector3(-0.5073f, 0.5073f, 1f);
            }
        }
    }
    
    static class TabletMainMenuHomeScoochButtonsCtorPatch
    {
        static public void Postfix(PickableMainMenuButton __instance)
        {
            GameObject more_button = GameObject.Find("main Buttons/Play More");
            if (more_button && more_button.transform.localScale.x != 1.015f)
            {
                Debug.Log("damn buttons will not move!!");
                //Adjust local button
                GameObject play_local = GameObject.Find("main Buttons/Play");
                play_local.transform.localPosition = new Vector2(-320f, play_local.transform.localPosition.y);
                play_local.transform.localScale = new Vector3(1.015f, 1, 1);

                //Adjust online button
                GameObject play_online = GameObject.Find("main Buttons/Play Online");
                play_online.transform.localScale = new Vector3(1.015f, 1, 1);

                //Adjust more button
                more_button.transform.localScale = new Vector3(1.015f, 1, 1);
                more_button.transform.localPosition = new Vector2(320f, more_button.transform.localPosition.y);
            }
        }
    }

    static class TabletMainMenuOnlineIndicatorCtorPatch
    {
        static public void Prefix(bool spinnerActive, bool buttonActive)
        {
            GameObject more_button = GameObject.Find("main Buttons/Play More");
            if (more_button)
            {
                more_button.transform.Find("LoadingSpinner")?.gameObject.SetActive(spinnerActive);
                TabletDisableGroup tdg = more_button.GetComponent<TabletDisableGroup>();
                if (tdg != null && more_button.GetComponent<TabletDisableGroup>().Disabled != !buttonActive)
                {
                    more_button.GetComponent<TabletDisableGroup>().SetDisabled(!buttonActive);
                    TabletButton tb = more_button.GetComponent<TabletButton>();
                    more_button.transform.Find("Image1").GetComponent<Image>().color = tb.labelImage.color;
                    more_button.transform.Find("Image2").GetComponent<Image>().color = tb.labelImage.color;
                }
            }
        }
    }
    
    static class GameSettingsVersionCtorPatch
    {
        static public bool Prefix(GameSettings __instance, ref string __result)
        {
            if (GameSettings.GetInstance().versionNumber.Contains(MorePlayersMod.mod_version_full))
            {
                __result = GameSettings.GetInstance().versionNumber;
                return false;
            }

            __result = I2.Loc.ScriptLocalization.CurrentVersion;
            return false;
        }
    }
}