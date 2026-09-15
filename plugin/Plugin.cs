using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using FistVR;
using HarmonyLib;

namespace Gundomizer
{
    [BepInPlugin("quaternion.gundomizer", "Gundomizer", "0.1.6")]
    [BepInProcess("h3vr.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> SpawnItemInstantly;
        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            try
            {
                SpawnItemInstantly = Config.Bind("General", "Spawn Item Instantly", true,
                    "Enabled: spawns the item when you click the button. " +
                    "Disabled: select the random item, use the spawner's Spawn button to spawn it.");
                SpawnerBridge.Validate();
                harmony = new Harmony("quaternion.gundomizer");
                harmony.Patch(AccessTools.Method(typeof(ItemSpawnerV2), "Start"),
                    postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterStart)));
                Logger.LogInfo("Gundomizer 0.1.6 loaded. Classic and tag viewer randomizer enabled.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Gundomizer could not bind this game version: " + ex);
            }
        }

        private static void AfterStart(ItemSpawnerV2 __instance)
        {
            try
            {
                if (__instance.GetComponent<RandomizerController>() == null)
                    __instance.gameObject.AddComponent<RandomizerController>().Initialize(__instance);
            }
            catch (Exception ex) { Log.LogError("Could not add Gundomizer buttons: " + ex); }
        }

        private void OnDestroy()
        {
            if (harmony != null) harmony.UnpatchSelf();
        }
    }
}
