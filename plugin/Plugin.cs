using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using FistVR;
using HarmonyLib;

namespace Gundomizer
{
    [BepInPlugin("quaternion.gundomizer", "Gundomizer", "1.1.0")]
    [BepInProcess("h3vr.exe")]
    [BepInDependency("h3vr.otherloader", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> SpawnItemInstantly;
        internal static ConfigEntry<bool> AutoFillHeldItem;
        internal static ConfigEntry<bool> PersistentIndex;
        internal static ConfigEntry<bool> ResetMetadataIndexing;
        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            try
            {
                // BepInEx preserves unbound keys. Remove just the retired pause settings
                // so existing profiles' config editors do not keep offering dead options.
                var orphaned = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(Config, null)
                    as System.Collections.Generic.IDictionary<ConfigDefinition, string>;
                if (orphaned != null)
                {
                    bool removed = orphaned.Remove(new ConfigDefinition("Performance", "New Prefab Loads Per Click"));
                    removed |= orphaned.Remove(new ConfigDefinition("Performance", "Search Seconds Per Click"));
                    if (removed) Config.Save();
                }
                SpawnItemInstantly = Config.Bind("General", "Spawn Item Instantly", true,
                    "Enabled: spawns the item when you click the button. " +
                    "Disabled: select the random item, use the panel's Spawn button to spawn it.");
                AutoFillHeldItem = Config.Bind("General", "Auto Fill Held Item", true,
                    "After spawning randomized ammo, replace existing rounds and fill the held magazine or weapon to capacity with that ammo. " +
                    "Also fills matching clips, speedloaders, and installed weapon magazines/chambers of the held item. " +
                    "In selection mode, filling happens only when you press Spawn.");
                PersistentIndex = Config.Bind("Performance", "Persistent Connector Index", true,
                    "Read and cache prefab connector metadata slowly in the background, without loading Unity assets. " +
                    "Only changed packages/bundles are rebuilt. Unindexed items keep the normal live checks. Restart the game after changing this setting.");
                ResetMetadataIndexing = Config.Bind("Performance", "Reset Metadata Indexing", false,
                    "Set true to discard Gundomizer's saved connector metadata and rebuild it in the background. " +
                    "Runs once and switches itself back to false. Applies during play when changed through a config manager, " +
                    "or on the next launch when edited with the game closed.");
                SpawnerBridge.Validate();
                OtherLoaderBridge.Initialize();
                harmony = new Harmony("quaternion.gundomizer");
                harmony.Patch(AccessTools.Method(typeof(ItemSpawnerV2), "Start"),
                    postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterStart)));
                harmony.Patch(AccessTools.Method(typeof(ItemSpawnerV2), "RedrawDetailsCanvas"),
                    postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterDetails)));
                if (OtherLoaderBridge.Active)
                    harmony.Patch(OtherLoaderBridge.SpawnHandler,
                        prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeOtherLoaderSpawn)));
                else harmony.Patch(AccessTools.Method(typeof(ItemSpawnerV2), "BTN_Details_Spawn"),
                    prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeSelectedSpawn)));
                harmony.Patch(AccessTools.Method(typeof(ItemSpawnerV2), "ExternalSpawnFromLaserToPoint"),
                    prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforePortableSpawn)));
                Logger.LogInfo("Gundomizer 1.1.0 loaded. Classic and tag viewer randomizer enabled.");
                ConnectorIndex.Start(this);
                try { PersistentConnectorIndex.Start(this); }
                catch (Exception ex) { Logger.LogWarning("Persistent indexing unavailable; live search remains enabled: " + ex); }
            }
            catch (Exception ex)
            {
                Logger.LogError("Gundomizer could not bind this game version: " + ex);
            }
        }

        private void Update()
        {
            // Config managers may raise change events off-thread. Consume requests here so
            // catalog startup and Unity state always stay on the game's main thread.
            if (ResetMetadataIndexing == null || !ResetMetadataIndexing.Value) return;
            try { PersistentConnectorIndex.Reset(this); }
            catch (Exception ex) { Logger.LogWarning("Metadata reset unavailable: " + ex.Message); }
            finally
            {
                try { ResetMetadataIndexing.Value = false; Config.Save(); }
                catch (Exception ex) { Logger.LogWarning("Could not save the one-shot reset setting: " + ex.Message); }
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
                // These entries support the native preview, history and Spawn button. They
                // aren't browser categories and shouldn't write synthetic IDs to favorites.
                if (AmmoSpawnerEntries.Owns(OtherLoaderBridge.Resolve(___m_selectedID)))
                    foreach (var button in __instance.IM_FavButtons) button.gameObject.SetActive(false);
            }
            catch (Exception ex) { Log.LogWarning("Could not refresh missing preview: " + ex.Message); }
        }

        private static bool BeforeSelectedSpawn(ItemSpawnerV2 __instance, string ___m_selectedID)
        {
            var controller = __instance.GetComponent<RandomizerController>();
            return controller == null || !controller.enabled || !controller.SpawnManagedSelection(___m_selectedID);
        }

        private static bool BeforePortableSpawn(ItemSpawnerV2 __instance, UnityEngine.Vector3 __0)
        {
            var controller = __instance.GetComponent<RandomizerController>();
            return controller == null || !controller.enabled || !controller.SpawnManagedSelectionAtPoint(__0);
        }

        private static bool BeforeOtherLoaderSpawn(ItemSpawnerV2 __0, ref bool __result)
        {
            // This HarmonyX build continues running competing prefixes after a false result.
            // Intercept OtherLoader's handler itself, so a managed click is spawned only once.
            var controller = __0.GetComponent<RandomizerController>();
            if (controller == null || !controller.enabled || !controller.SpawnManagedSelection()) return true;
            __result = false;
            return false;
        }

        private void OnDestroy()
        {
            PersistentConnectorIndex.Stop();
            AmmoSpawnerEntries.Clear();
            if (harmony != null) harmony.UnpatchSelf();
        }
    }
}
