using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using FistVR;
using HarmonyLib;

namespace Gundomizer
{
    [BepInPlugin("quaternion.gundomizer", "Gundomizer", "0.1.8")]
    [BepInProcess("h3vr.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> SpawnItemInstantly;
        internal static ConfigEntry<int> MaxNewLoads;
        internal static ConfigEntry<float> SearchSeconds;
        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            try
            {
                SpawnItemInstantly = Config.Bind("General", "Spawn Item Instantly", true,
                    "Enabled: spawns the item when you click the button. " +
                    "Disabled: select the random item, use the spawner's Spawn button to spawn it.");
                MaxNewLoads = Config.Bind("Performance", "New Prefab Loads Per Click", 8,
                    new ConfigDescription("Pause a search after this many new prefab requests. Click the same button to continue the same shuffled search. " +
                    "Lower values limit speculative loading with large mod collections; one prefab can still require a large bundle.", new AcceptableValueRange<int>(1, 64)));
                SearchSeconds = Config.Bind("Performance", "Search Seconds Per Click", 10f,
                    new ConfigDescription("Pause a search after this many seconds. An already-started shared game load continues; clicking again resumes the search.",
                    new AcceptableValueRange<float>(1f, 60f)));
                SpawnerBridge.Validate();
                harmony = new Harmony("quaternion.gundomizer");
                harmony.Patch(AccessTools.Method(typeof(ItemSpawnerV2), "Start"),
                    postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterStart)));
                harmony.Patch(AccessTools.Method(typeof(ItemSpawnerV2), "RedrawDetailsCanvas"),
                    postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterDetails)));
                Logger.LogInfo("Gundomizer 0.1.8 loaded. Classic and tag viewer randomizer enabled.");
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
                ConnectorIndex.Start(BepInEx.Bootstrap.Chainloader.PluginInfos["quaternion.gundomizer"].Instance);
            }
            catch (Exception ex) { Log.LogError("Could not add Gundomizer buttons: " + ex); }
        }

        private static void AfterDetails(ItemSpawnerV2 __instance, string ___m_selectedID)
        {
            try
            {
                var controller = __instance.GetComponent<RandomizerController>();
                if (controller != null && controller.enabled) controller.RefreshPreview(___m_selectedID);
            }
            catch (Exception ex) { Log.LogWarning("Could not refresh missing preview: " + ex.Message); }
        }

        private void OnDestroy()
        {
            if (harmony != null) harmony.UnpatchSelf();
        }
    }
}
