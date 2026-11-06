using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
            message = "Slot unavailable.";
            try
            {
                if (!wholeBuild && IsBusy(menu)) { message = "Build roll in progress."; return false; }
                if (wholeBuild && (bool)Read(menu, "_isShowingSkins"))
                    AccessTools.Method(menu.GetType(), "PButton_HideSkins").Invoke(menu, null);
                object definition;
                if (!TryDefinition(menu, out definition, out message)) return false;
                var type = menu.GetType();
                string selected;
                if (wholeBuild) selected = (string)AccessTools.Method(definition.GetType(), "GetRandomPart").Invoke(definition, null);
                else
                {
                    var pool = (IList<string>)AccessTools.Property(definition.GetType(), "TakeAndHoldSpawnPool").GetValue(definition, null);
                    int choice = SelectionPolicy.AlternativeIndex(pool, CurrentPoint(menu), n => UnityEngine.Random.Range(0, n));
                    if (choice < 0) { message = "No other parts."; return false; }
                    selected = pool[choice];
                }

                // The normal menu action applies only this slot. In particular, do not use
                // isRandomized=true: that also randomizes nested parts and skins in MW.
                AccessTools.Method(type, "InitializeArrays").Invoke(menu, null);
                var names = (string[])Read(menu, "_partNames");
                int index = Array.IndexOf(names, selected);
                int pageSize = ((GameObject[])Read(menu, "PartButtons")).Length;
                if (index < 0 || pageSize == 0) { message = "Part unavailable."; return false; }
                AccessTools.Field(type, "_pageIndex").SetValue(menu, index / pageSize);
                AccessTools.Method(type, "PButton_Select").Invoke(menu, new object[] { index % pageSize });
                message = "Selected " + selected + ".";
                return true;
            }
            catch (Exception ex)
            {
                var cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Plugin.Log.LogWarning("Modular slot roll failed: " + cause);
                message = "Part roll failed. See log.";
                return false;
            }
        }

        private static bool TryDefinition(MonoBehaviour menu, out object definition, out string message, bool wholeBuild = false)
        {
            definition = null; message = "Slot unavailable.";
            if (menu == null) return false;
            var type = menu.GetType();
            while (type != null && type.FullName != "ModularWorkshop.ModularWorkshopUI") type = type.BaseType;
            if (type == null) return false;
            if ((bool)Read(menu, "_skinOnlyMode") || (!wholeBuild && (bool)Read(menu, "_isShowingSkins")))
            { message = "Open the parts menu."; return false; }
            object target = Read(menu, "ModularWeapon") ?? Read(menu, "ModularPhysicalObject");
            if (target == null || (target is UnityEngine.Object && (UnityEngine.Object)target == null)) return false;
            string group = (string)Read(menu, "ModularPartsGroupID");
            var points = (IDictionary)AccessTools.Property(target.GetType(), "AllAttachmentPoints").GetValue(target, null);
            if (!points.Contains(group)) { message = "Slot unavailable."; return false; }
            var point = points[group];
            if ((bool)Read(point, "IsPointDisabled") || (bool)Read(point, "DisallowTakeAndHoldRandomization"))
            { message = "Slot excluded."; return false; }
            var manager = type.Assembly.GetType("ModularWorkshop.ModularWorkshopManager", true);
            var groups = (IDictionary)AccessTools.Field(manager, "ModularWorkshopPartsGroupsDictionary").GetValue(null);
            if (!groups.Contains(group)) { message = "Parts unavailable."; return false; }
            definition = groups[group];
            var pool = (IList)AccessTools.Property(definition.GetType(), "TakeAndHoldSpawnPool").GetValue(definition, null);
            if (pool.Count == 0) { message = "No eligible parts."; return false; }
            if (!wholeBuild && !pool.Cast<string>().Any(id => id != (string)Read(point, "SelectedModularWeaponPart")))
            { message = "No other parts."; return false; }
            return true;
        }

        private static object Point(MonoBehaviour menu)
        {
            object target = Read(menu, "ModularWeapon") ?? Read(menu, "ModularPhysicalObject");
            if (target == null) return null;
            var points = (IDictionary)AccessTools.Property(target.GetType(), "AllAttachmentPoints").GetValue(target, null);
            return points[(string)Read(menu, "ModularPartsGroupID")];
        }

        private static string CurrentPoint(MonoBehaviour menu) => (string)Read(Point(menu), "SelectedModularWeaponPart");

        internal static bool CanRandomizeSkin(MonoBehaviour menu, bool wholeBuild = false)
        {
            string[] names; string current;
            try { return SkinChoices(menu, wholeBuild, out names, out current) && names.Any(n => n != current); }
            catch { return false; }
        }

        private static bool SkinChoices(MonoBehaviour menu, bool wholeBuild, out string[] names, out string current)
        {
            names = null; current = null;
            if (menu == null) return false;
            string path;
            if ((bool)Read(menu, "_receiverSkinMode"))
            {
                var target = (bool)Read(menu, "_receiverSkinSystemMode") ? Read(menu, "SkinSystem") : Read(menu, "ModularPhysicalObject");
                var weapon = Read(menu, "ModularWeapon");
                if (weapon != null) target = AccessTools.Property(weapon.GetType(), "GetModularFVRFireArm").GetValue(weapon, null);
                if (target == null) return false;
                path = (string)Read(target, "SkinPath"); current = (string)Read(target, "CurrentSelectedReceiverSkinID");
            }
            else
            {
                var point = Point(menu);
                if (point == null || (bool)Read(point, "IsPointDisabled") || (wholeBuild && (bool)Read(point, "DisallowTakeAndHoldRandomization"))) return false;
                path = (string)Read(menu, "ModularPartsGroupID") + "/" + Read(point, "SelectedModularWeaponPart");
                current = (string)AccessTools.Property(point.GetType(), "CurrentSkin").GetValue(point, null);
            }
            var manager = menu.GetType().Assembly.GetType("ModularWorkshop.ModularWorkshopManager", true);
            var definitions = (IDictionary)AccessTools.Field(manager, "ModularWorkshopSkinsDictionary").GetValue(null);
            if (!definitions.Contains(path)) return false;
            var skins = (IDictionary)AccessTools.Property(definitions[path].GetType(), "SkinDictionary").GetValue(definitions[path], null);
            names = skins.Keys.Cast<string>().ToArray();
            return names.Length > 0;
        }

        internal static bool TryRandomizeSkin(MonoBehaviour menu, out string message) => RandomizeSkin(menu, false, out message);
        internal static bool TryRandomizeBuildSkin(MonoBehaviour menu, out string message) => RandomizeSkin(menu, true, out message);

        private static bool RandomizeSkin(MonoBehaviour menu, bool wholeBuild, out string message)
        {
            message = "No other skins.";
            try
            {
                if (!wholeBuild && IsBusy(menu)) { message = "Build roll in progress."; return false; }
                string[] names; string current;
                if (!SkinChoices(menu, wholeBuild, out names, out current)) return false;
                int choice = SelectionPolicy.AlternativeIndex(names, current, n => UnityEngine.Random.Range(0, n));
                if (choice < 0) return false;
                bool wasShowingSkins = (bool)Read(menu, "_isShowingSkins");
                try
                {
                    if (!wasShowingSkins) AccessTools.Method(menu.GetType(), "PButton_ShowSkins").Invoke(menu, null);
                    int index = Array.IndexOf((string[])Read(menu, "_skinNames"), names[choice]);
                    int size = ((GameObject[])Read(menu, "PartButtons")).Length;
                    if (index < 0 || size == 0) return false;
                    AccessTools.Field(menu.GetType(), "_skinPageIndex").SetValue(menu, index / size);
                    AccessTools.Method(menu.GetType(), "PButton_Select").Invoke(menu, new object[] { index % size });
                    message = "Selected skin " + names[choice] + ".";
                    return true;
                }
                finally { if (!wasShowingSkins && menu != null) AccessTools.Method(menu.GetType(), "PButton_HideSkins").Invoke(menu, null); }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Modular skin roll failed: " + ex);
                message = "Skin roll failed. See log.";
                return false;
            }
        }

        internal static object Read(object target, string name) => AccessTools.Field(target.GetType(), name).GetValue(target);
    }
}
