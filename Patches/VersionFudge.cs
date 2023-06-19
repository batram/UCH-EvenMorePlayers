using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace MorePlayers
{
    [HarmonyPatch(typeof(TabletMainMenuHome), nameof(TabletMainMenuHome.Update))]
    static class TabletMainMenuHomeUpdateCtorPatch
    {
        static void Postfix(TabletMainMenuHome __instance)
        {
            bool needs_update = !MorePlayersMod.og_version.StartsWith(GameState.GetLocalizationVersionNumber());
            __instance.showingPleaseUpdate = needs_update;
            __instance.pleaseUpdateButton.gameObject.SetActive(needs_update);
        }
    }
}
