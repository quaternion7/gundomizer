using System;
using System.Collections.Generic;
using FistVR;
using UnityEngine;

namespace Gundomizer
{
    // Uses the same native refill operations as the ammo panel, plus chamber loading.
    // Only run after the loose round has successfully spawned. No catalog scans or loads.
    internal sealed class AmmoFill
    {
        private readonly FVRFireArmRound round;
        private readonly HashSet<Component> visited = new HashSet<Component>();
        internal int Filled { get; private set; }
        internal int Failed { get; private set; }

        private AmmoFill(FVRFireArmRound round) { this.round = round; }

        internal static AmmoFill Apply(FVRPhysicalObject held, FVRFireArmRound round)
        {
            var fill = new AmmoFill(round);
            if (held == null || round == null || round.IsSpent) return fill;
            try
            {
                foreach (var obj in Compatibility.Objects(held))
                {
                    fill.Magazine(obj as FVRFireArmMagazine);
                    fill.Clip(obj as FVRFireArmClip);
                    var loader = obj as Speedloader;
                    if (loader != null && loader.Chambers != null)
                        foreach (var chamber in loader.Chambers)
                            if (chamber != null && chamber.Type == round.RoundType)
                                fill.Try(chamber, () => chamber.Load(round.RoundClass));
                    fill.Firearm(obj as FVRFireArm);
                    var attachable = obj as AttachableFirearmPhysicalObject;
                    if (attachable != null) fill.Attachable(attachable.FA);
                }
            }
            catch (Exception ex) { fill.Report(ex); }
            return fill;
        }

        private void Magazine(FVRFireArmMagazine magazine)
        {
            if (magazine != null && magazine.RoundType == round.RoundType)
                Try(magazine, () => magazine.ReloadMagWithType(round.RoundClass));
        }

        private void Clip(FVRFireArmClip clip)
        {
            if (clip != null && clip.RoundType == round.RoundType)
                Try(clip, () => clip.ReloadClipWithType(round.RoundClass));
        }

        private void Chamber(FVRFireArmChamber chamber)
        {
            if (chamber != null && chamber.RoundType == round.RoundType)
                // SetRound copies the round's native reference; it does not consume the loose round.
                Try(chamber, () => chamber.SetRound(round, false));
        }

        private void Firearm(FVRFireArm firearm)
        {
            if (firearm == null || !visited.Add(firearm)) return;
            Magazine(firearm.Magazine);
            if (firearm.SecondaryMagazineSlots != null)
                foreach (var slot in firearm.SecondaryMagazineSlots) if (slot != null) Magazine(slot.Magazine);
            Clip(firearm.Clip);
            var chambers = firearm.GetChambers();
            if (chambers != null) foreach (var chamber in chambers) Chamber(chamber);
            Attachable(firearm.GetIntegratedAttachableFirearm());
            if (firearm.RoundType == round.RoundType && firearm.UsesBelts && firearm.BeltDD != null)
                Try(firearm.BeltDD, () =>
                {
                    // Preserve the connected belt's length and physical state; replace its
                    // existing feed rounds along with the box that was filled above.
                    foreach (var loaded in firearm.BeltDD.BeltRounds)
                    {
                        loaded.LR_Class = round.RoundClass;
                        loaded.LR_Mesh = AM.GetRoundMesh(round.RoundType, round.RoundClass);
                        loaded.LR_Material = AM.GetRoundMaterial(round.RoundType, round.RoundClass);
                        loaded.LR_ObjectWrapper = AM.GetRoundSelfPrefab(round.RoundType, round.RoundClass);
                    }
                    firearm.BeltDD.UpdateProxyRounds(0);
                });
        }

        private void Attachable(AttachableFirearm firearm)
        {
            if (firearm == null || !visited.Add(firearm)) return;
            Firearm(firearm.OverrideFA);
            Magazine(firearm.Magazine);
            if (firearm.SecondaryMagazineSlots != null)
                foreach (var slot in firearm.SecondaryMagazineSlots) if (slot != null) Magazine(slot.Magazine);
            Clip(firearm.Clip);
            foreach (var chamber in firearm.GetComponentsInChildren<FVRFireArmChamber>(true))
                if (chamber.GetComponentInParent<AttachableFirearm>() == firearm) Chamber(chamber);
        }

        private void Try(Component component, Action fill)
        {
            if (!visited.Add(component)) return;
            try { fill(); ++Filled; }
            catch (Exception ex) { Report(ex); }
        }

        private void Report(Exception ex)
        {
            ++Failed;
            Plugin.Log.LogWarning("Ammo spawned, but a held-item refill failed: " + ex);
        }
    }
}
