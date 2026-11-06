using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Gundomizer
{
    // Optional integration: native pools and the native menu do all selection/application
    // work. No ModularWorkshop assembly reference, asset scan, or UI hooks are required.
    internal static class ModularWorkshopBridge
    {
        internal static bool TryRandomizeSlot(MonoBehaviour menu, out string message)
        {
            message = "No modular part menu available.";
            if (menu == null) return false;
            var type = menu.GetType();
            while (type != null && type.FullName != "ModularWorkshop.ModularWorkshopUI") type = type.BaseType;
            if (type == null) return false;
            try
            {
                if ((bool)Read(menu, "_skinOnlyMode") || (bool)Read(menu, "_isShowingSkins"))
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
                var definition = groups[group];
                var pool = (IList)AccessTools.Property(definition.GetType(), "TakeAndHoldSpawnPool").GetValue(definition, null);
                if (pool.Count == 0) { message = "No parts in this slot's randomization pool."; return false; }
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

        private static object Read(object target, string name) => AccessTools.Field(target.GetType(), name).GetValue(target);
    }
}
