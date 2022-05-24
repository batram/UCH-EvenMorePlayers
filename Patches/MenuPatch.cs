using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

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

            var accept_original = typeof(TabletButton).GetMethod(nameof(TabletButton.OnAccept));
            var accept_prefix = typeof(TabletButtonOnAcceptCtorPatch).GetMethod(nameof(TabletButtonOnAcceptCtorPatch.Prefix));
            harmony.Patch(accept_original, prefix: new HarmonyMethod(accept_prefix));
        }
    }

    //TabletButton::OnAccept(PickCursor pickCursor)
    static class TabletButtonOnAcceptCtorPatch
    {
        static public void Prefix(TabletButton __instance)
        {
            Debug.Log("Pressed da button: " + __instance.gameObject.name);
            string mod_version = " [EvenMorePlayers: " + MorePlayersMod.mod_version + "]";

            if (__instance.gameObject.name == "Play More")
            {
                if (!GameSettings.GetInstance().versionNumber.Contains(mod_version))
                {
                    GameSettings.GetInstance().versionNumber += mod_version;
                    if(PlayerManager.maxPlayers != MorePlayersMod.newPlayerLimit)
                    {
                        PlayerManager.maxPlayers = MorePlayersMod.newPlayerLimit;
                        new Harmony("notfood.MorePlayers.PlayerNumPatch").PatchAll();
                    }
                }

            } else if (__instance.gameObject.name == "Play Online")
            {
                GameSettings.GetInstance().versionNumber = MorePlayersMod.og_version;
                PlayerManager.maxPlayers = 4;
                Harmony.UnpatchAll();
                MenuPatch.PatchMenu();
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
                more_button.transform.Find("LoadingSpinner").gameObject.SetActive(false);
                var more_label = more_button.transform.Find("Text Label").GetComponent<TabletTextLabel>();
                more_label.text = "More";
                more_label.transform.position -= new Vector3(0.6655f, 0f, 0f);

                var more_image = more_button.transform.Find("Image");
                more_image.transform.localScale = new Vector3(-0.5073f, 0.5073f, 1f);
                more_image.transform.position += new Vector3(-0.06f, 0.5073f, 0f);
                var more_image1 = Object.Instantiate<GameObject>(more_image.gameObject, more_image.transform.parent);
                more_image1.transform.position = more_image.transform.position;
                more_image1.transform.position -= new Vector3(0.6f, 1.2f, 0f);
                more_image1.transform.localScale = new Vector3(-0.5073f, 0.5073f, 1f);

                var more_image2 = Object.Instantiate<GameObject>(more_image.gameObject, more_image.transform.parent);
                more_image2.transform.position = more_image.transform.position;
                more_image2.transform.position -= new Vector3(-0.6f, 1.2f, 0f);
                more_image2.transform.localScale = new Vector3(-0.5073f, 0.5073f, 1f);
            }

        }
    }
}