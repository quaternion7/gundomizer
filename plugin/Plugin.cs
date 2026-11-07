using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using FistVR;
using HarmonyLib;

namespace Gundomizer
{
    [BepInPlugin("quaternion.gundomizer", "Gundomizer", "1.2.0")]
    [BepInProcess("h3vr.exe")]
    [BepInDependency("h3vr.otherloader", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("h3vr.cityrobo.ModularWorkshopManager", BepInDependency.DependencyFlags.SoftDependency)]
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
                    "ON: spawn on roll. OFF: preview; press Spawn to accept.");
                AutoFillHeldItem = Config.Bind("General", "Auto Fill Held Item", true,
                    "Fill the held item with rolled ammo, replacing loaded rounds. Also spawns the loose round. " +
                    "In preview mode, fills when you press Spawn.");
                PersistentIndex = Config.Bind("Performance", "Persistent Connector Index", true,
                    "Cache compatibility metadata in the background. Update changed packages; use live checks for uncached items. Restart required.");
                ResetMetadataIndexing = Config.Bind("Performance", "Reset Metadata Indexing", false,
                    "Clear and rebuild the saved index. Switches itself off after running.");
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
                try { ModularWorkshopBridge.Initialize(harmony); }
                catch (Exception ex) { Logger.LogWarning("Optional Modular Workshop controls unavailable: " + ex.Message); }
                Logger.LogInfo("Gundomizer 1.2.0 loaded. Classic and tag viewer randomizer enabled.");
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
