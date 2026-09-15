using System;
using System.Collections.Generic;
using FistVR;
using UnityEngine;

namespace Gundomizer
{
    internal static class AmmoCatalog
    {
        internal sealed class Variant
        {
            internal FireArmRoundType Type;
            internal FireArmRoundClass Class;
            internal string Key;
            internal string Name;
            internal string Caliber;
            internal string Properties;
            internal ItemSpawnerID Entry;
        }

        internal static HashSet<FireArmRoundType> Types(FVRPhysicalObject held)
        {
            var types = new HashSet<FireArmRoundType>();
            if (held == null) return types;
            foreach (var item in Compatibility.Objects(held))
            {
                var firearm = item as FVRFireArm;
                if (firearm != null && !(firearm is FlintlockWeapon))
                {
                    types.Add(firearm.RoundType);
                    var chambers = firearm.GetChambers();
                    if (chambers != null) foreach (var chamber in chambers) if (chamber != null) types.Add(chamber.RoundType);
                    var integrated = firearm.GetIntegratedAttachableFirearm();
                    if (integrated != null) types.Add(integrated.RoundType);
                }
                var attachable = item as AttachableFirearmPhysicalObject;
                if (attachable != null && attachable.FA != null) types.Add(attachable.FA.RoundType);
                var magazine = item as FVRFireArmMagazine;
                if (magazine != null) types.Add(magazine.RoundType);
                var clip = item as FVRFireArmClip;
                if (clip != null) types.Add(clip.RoundType);
                var loader = item as Speedloader;
                if (loader != null && loader.Chambers != null)
                    foreach (var chamber in loader.Chambers) if (chamber != null) types.Add(chamber.Type);
                var round = item as FVRFireArmRound;
                if (round != null) types.Add(round.RoundType);
            }
            return types;
        }

        internal static List<Variant> Read(HashSet<FireArmRoundType> types)
        {
            var result = new List<Variant>();
            foreach (var type in types)
            {
                Dictionary<FireArmRoundClass, FVRFireArmRoundDisplayData.DisplayDataClass> classes;
                if (!AM.STypeDic.TryGetValue(type, out classes)) continue;
                FVRFireArmRoundDisplayData caliberData;
                string caliber = AM.SRoundDisplayDataDic.TryGetValue(type, out caliberData) ? caliberData.DisplayName : type.ToString();
                foreach (var pair in classes)
                {
                    var data = pair.Value;
                    if (data == null || data.ObjectID == null || string.IsNullOrEmpty(data.ObjectID.SpawnedFromId)
                        || !IM.HasSpawnedID(data.ObjectID.SpawnedFromId)) continue;
                    var entry = IM.GetSpawnerID(data.ObjectID.SpawnedFromId);
                    // Selection must identify this exact variant, not a different default cartridge.
                    if (!SpawnerBridge.IsAvailable(entry) || entry.MainObject != data.ObjectID) continue;
                    var properties = new List<string>();
                    foreach (var tag in AM.TagDic)
                    {
                        List<FireArmRoundClass> tagged;
                        if (tag.Key != FireArmRoundPropertyTag.None && tag.Value != null && tag.Value.TryGetValue(type, out tagged)
                            && tagged != null && tagged.Contains(pair.Key))
                            properties.Add(System.Text.RegularExpressions.Regex.Replace(tag.Key.ToString(), "([a-z])([A-Z])", "$1 $2").Replace('_', ' '));
                    }
                    properties.Sort(StringComparer.Ordinal);
                    result.Add(new Variant { Type = type, Class = pair.Key, Key = AmmoSelection.Key((int)type, (int)pair.Key),
                        Name = string.IsNullOrEmpty(data.Name) ? pair.Key.ToString() : data.Name,
                        Caliber = caliber, Properties = string.Join(", ", properties.ToArray()), Entry = entry });
                }
            }
            result.Sort((a, b) => { int c = string.CompareOrdinal(a.Caliber, b.Caliber); return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name); });
            return result;
        }

        internal static bool Matches(FVRPhysicalObject held, GameObject prefab, Variant variant)
        {
            var round = prefab == null ? null : prefab.GetComponent<FVRFireArmRound>();
            return round != null && round.RoundType == variant.Type && round.RoundClass == variant.Class
                && Types(held).Contains(variant.Type) && AmmoSelection.Shared.Includes(variant.Key);
        }
    }
}
