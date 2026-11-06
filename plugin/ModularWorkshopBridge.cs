using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Gundomizer
{
    // Optional integration: native pools and the native menu do all selection/application
    // work. No ModularWorkshop assembly reference or asset scan is required.
    internal static class ModularWorkshopBridge
    {
        internal static void Initialize(Harmony harmony)
        {
            BepInEx.PluginInfo info;
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("h3vr.cityrobo.ModularWorkshopManager", out info)) return;
            var type = info.Instance.GetType().Assembly.GetType("ModularWorkshop.ModularWorkshopUI", true);
            harmony.Patch(AccessTools.Method(type, "UpdateDisplay"),
                postfix: new HarmonyMethod(typeof(ModularWorkshopBridge), nameof(AfterDisplay)));
            harmony.Patch(AccessTools.Method(type.Assembly.GetType("ModularWorkshop.ModularWorkshopPlatform", true), "Update"),
                postfix: new HarmonyMethod(typeof(ModularWorkshopBridge), nameof(AfterPlatformUpdate)));
            Plugin.Log.LogInfo("Enabled optional Modular Workshop randomizer controls.");
        }

        private static void AfterPlatformUpdate(MonoBehaviour __instance)
        {
            var control = __instance.GetComponent<WorkshopBuildControl>() ?? __instance.gameObject.AddComponent<WorkshopBuildControl>();
            if (!control.enabled) return;
            try { control.Refresh(__instance); }
            catch (Exception ex)
            {
                control.enabled = false;
                Plugin.Log.LogWarning("Could not refresh modular build control: " + ex);
            }
        }

        internal static bool IsBusy(MonoBehaviour menu)
        {
            object target = Read(menu, "ModularWeapon") ?? Read(menu, "ModularPhysicalObject");
            var platform = target == null ? null : AccessTools.Property(target.GetType(), "WorkshopPlatform").GetValue(target, null) as MonoBehaviour;
            var control = platform == null ? null : platform.GetComponent<WorkshopBuildControl>();
            return control != null && control.Busy;
        }

        private static void AfterDisplay(MonoBehaviour __instance)
        {
            try
            {
                if ((bool)Read(__instance, "_skinOnlyMode")) return;
                var control = __instance.GetComponent<WorkshopSlotControl>() ?? __instance.gameObject.AddComponent<WorkshopSlotControl>();
                control.Refresh(__instance);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Could not refresh modular slot control: " + ex.Message); }
        }

        internal static bool CanRandomizeSlot(MonoBehaviour menu, bool wholeBuild = false)
        {
            object definition; string message;
            try { return TryDefinition(menu, out definition, out message, wholeBuild); }
            catch { return false; }
        }

        internal static bool TryRandomizeSlot(MonoBehaviour menu, out string message)
            => RandomizeSlot(menu, false, out message);

        internal static bool TryRandomizeBuildSlot(MonoBehaviour menu, out string message)
            => RandomizeSlot(menu, true, out message);

        private static bool RandomizeSlot(MonoBehaviour menu, bool wholeBuild, out string message)
        {
            message = "No modular part menu available.";
            try
            {
                if (!wholeBuild && IsBusy(menu)) { message = "Build randomization is in progress."; return false; }
                if (wholeBuild && (bool)Read(menu, "_isShowingSkins"))
                    AccessTools.Method(menu.GetType(), "PButton_HideSkins").Invoke(menu, null);
                object definition;
                if (!TryDefinition(menu, out definition, out message)) return false;
                var type = menu.GetType();
                string selected = (string)AccessTools.Method(definition.GetType(), "GetRandomPart").Invoke(definition, null);

                // The normal menu action applies only this slot. In particular, do not use
                // isRandomized=true: that also randomizes nested parts and skins in MW.
                AccessTools.Method(type, "InitializeArrays").Invoke(menu, null);
                var names = (string[])Read(menu, "_partNames");
                int index = Array.IndexOf(names, selected);
                int pageSize = ((GameObject[])Read(menu, "PartButtons")).Length;
                if (index < 0 || pageSize == 0) { message = "Randomized part is unavailable in this menu."; return false; }
                AccessTools.Field(type, "_pageIndex").SetValue(menu, index / pageSize);
                AccessTools.Method(type, "PButton_Select").Invoke(menu, new object[] { index % pageSize });
                message = "Selected " + selected + ".";
                return true;
            }
            catch (Exception ex)
            {
                var cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Plugin.Log.LogWarning("Modular slot roll failed: " + cause);
                message = "Could not complete the modular slot change. Check logs.";
                return false;
            }
        }

        private static bool TryDefinition(MonoBehaviour menu, out object definition, out string message, bool wholeBuild = false)
        {
            definition = null; message = "No modular part menu available.";
            if (menu == null) return false;
            var type = menu.GetType();
            while (type != null && type.FullName != "ModularWorkshop.ModularWorkshopUI") type = type.BaseType;
            if (type == null) return false;
            if ((bool)Read(menu, "_skinOnlyMode") || (!wholeBuild && (bool)Read(menu, "_isShowingSkins")))
            { message = "Choose a parts menu to randomize this slot."; return false; }
            object target = Read(menu, "ModularWeapon") ?? Read(menu, "ModularPhysicalObject");
            if (target == null || (target is UnityEngine.Object && (UnityEngine.Object)target == null)) return false;
            string group = (string)Read(menu, "ModularPartsGroupID");
            var points = (IDictionary)AccessTools.Property(target.GetType(), "AllAttachmentPoints").GetValue(target, null);
            if (!points.Contains(group)) { message = "This modular slot is no longer available."; return false; }
            var point = points[group];
            if ((bool)Read(point, "IsPointDisabled") || (bool)Read(point, "DisallowTakeAndHoldRandomization"))
            { message = "This modular slot is excluded from randomization."; return false; }
            var manager = type.Assembly.GetType("ModularWorkshop.ModularWorkshopManager", true);
            var groups = (IDictionary)AccessTools.Field(manager, "ModularWorkshopPartsGroupsDictionary").GetValue(null);
            if (!groups.Contains(group)) { message = "Modular parts are still unavailable."; return false; }
            definition = groups[group];
            var pool = (IList)AccessTools.Property(definition.GetType(), "TakeAndHoldSpawnPool").GetValue(definition, null);
            if (pool.Count == 0) { message = "No parts in this slot's randomization pool."; return false; }
            return true;
        }

        internal static object Read(object target, string name) => AccessTools.Field(target.GetType(), name).GetValue(target);
    }
}
