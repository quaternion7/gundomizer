using System;
using System.Collections.Generic;
using FistVR;
using UnityEngine;

namespace Gundomizer
{
    internal static class Compatibility
    {
        internal static FVRPhysicalObject HeldItem(FVRViveHand pointingHand = null)
        {
            if (pointingHand != null)
                return pointingHand.OtherHand == null ? null : Resolve(pointingHand.OtherHand.CurrentInteractable);
            if (GM.CurrentMovementManager == null || GM.CurrentMovementManager.Hands == null) return null;
            FVRPhysicalObject held = null;
            foreach (var hand in GM.CurrentMovementManager.Hands)
            {
                if (hand == null) continue;
                var item = Resolve(hand.CurrentInteractable);
                if (item == null) continue;
                if (held != null && held != item) return null; // No ambiguous target outside normal pointing UX.
                held = item;
            }
            return held;
        }

        private static FVRPhysicalObject Resolve(FVRInteractiveObject item)
        {
            if (item == null) return null;
            // Alternate grips may be held instead of the firearm's primary interactive component.
            return item as FVRPhysicalObject ?? item.GetComponentInParent<FVRPhysicalObject>();
        }

        internal static string Name(FVRPhysicalObject item)
        {
            if (item == null) return "none";
            return item.ObjectWrapper != null && !string.IsNullOrEmpty(item.ObjectWrapper.DisplayName)
                ? item.ObjectWrapper.DisplayName : item.name.Replace("(Clone)", "").Trim();
        }

        internal static bool CouldMatch(FVRObject candidate)
        {
            if (candidate == null) return false;
            switch (candidate.Category)
            {
                case FVRObject.ObjectCategory.Firearm:
                case FVRObject.ObjectCategory.Attachment:
                case FVRObject.ObjectCategory.Magazine:
                case FVRObject.ObjectCategory.Clip:
                case FVRObject.ObjectCategory.SpeedLoader: return true;
                default: return false; // Bullet selection and unrelated categories are not compatibility claims.
            }
        }

        internal static void Prefilter(List<ItemSpawnerID> candidates, FVRPhysicalObject held)
        {
            var magazineTypes = new HashSet<int>();
            var clipTypes = new HashSet<int>();
            var loaderIds = new HashSet<string>(StringComparer.Ordinal);
            bool hasMount = false;
            foreach (var mount in Mounts(held)) { hasMount = true; break; }
            foreach (var well in held.GetComponentsInChildren<FVRFireArmReloadTriggerWell>(true))
            {
                if (!BelongsTo(well.transform, held)) continue;
                if (well.UsesTypeOverride) magazineTypes.Add((int)well.TypeOverride);
                else if (well.IsAttachableWell && well.AFireArm != null) magazineTypes.Add((int)well.AFireArm.MagazineType);
                else if (well.FireArm != null) magazineTypes.Add((int)well.FireArm.MagazineType);
            }
            foreach (var well in held.GetComponentsInChildren<FVRFireArmClipTriggerWell>(true))
                if (BelongsTo(well.transform, held) && well.FireArm != null) clipTypes.Add((int)well.FireArm.ClipType);
            foreach (var target in Objects(held))
                if (target.ObjectWrapper != null && target.ObjectWrapper.CompatibleSpeedLoaders != null)
                    foreach (var loader in target.ObjectWrapper.CompatibleSpeedLoaders)
                        if (loader != null) loaderIds.Add(loader.ItemID);
            bool reverse = held is FVRFireArmMagazine || held is FVRFireArmClip || held is Speedloader
                || held is FVRFireArmAttachment;
            for (int i = candidates.Count - 1; i >= 0; --i)
            {
                var obj = candidates[i].MainObject;
                bool possible = CouldMatch(obj);
                switch (obj.Category)
                {
                    case FVRObject.ObjectCategory.Attachment: possible &= hasMount; break;
                    case FVRObject.ObjectCategory.Magazine:
                        possible &= magazineTypes.Count > 0 && ((int)obj.MagazineType == 0 || magazineTypes.Contains((int)obj.MagazineType)); break;
                    case FVRObject.ObjectCategory.Clip:
                        possible &= clipTypes.Count > 0 && ((int)obj.ClipType == 0 || clipTypes.Contains((int)obj.ClipType)); break;
                    case FVRObject.ObjectCategory.SpeedLoader: possible &= loaderIds.Contains(obj.ItemID); break;
                    case FVRObject.ObjectCategory.Firearm: possible &= reverse; break;
                }
                if (!possible) candidates.RemoveAt(i);
            }
        }

        internal static bool Matches(FVRPhysicalObject held, GameObject candidate)
        {
            if (held == null || candidate == null) return false;
            var item = candidate.GetComponent<FVRPhysicalObject>();
            if (item == null) return false;
            if (FitsOnto(item, held)) return true;
            // A held magazine/clip/attachment can also filter the Firearms section.
            if (item is FVRFireArm && FitsOnto(held, item)) return true;
            return false;
        }

        private static bool FitsOnto(FVRPhysicalObject candidate, FVRPhysicalObject target)
        {
            var attachment = candidate as FVRFireArmAttachment;
            if (attachment != null)
            {
                if (!attachment.CanAttach()) return false;
                foreach (var mount in Mounts(target))
                    if (mount.Type == attachment.Type && mount.isMountableOn(attachment)) return true;
            }

            var magazine = candidate as FVRFireArmMagazine;
            if (magazine != null)
            {
                // Use the same type override, belt-box and secondary/attachable-well rules as
                // FVRFireArmReloadTriggerMag. An occupied well still accepts a spare after unloading.
                if (candidate.GetComponentInChildren<FVRFireArmReloadTriggerMag>(true) == null) return false;
                foreach (var well in target.GetComponentsInChildren<FVRFireArmReloadTriggerWell>(true))
                {
                    if (!BelongsTo(well.transform, target)) continue;
                    var firearm = well.FireArm;
                    var attachable = well.AFireArm;
                    if (well.IsAttachableWell ? attachable == null : firearm == null) continue;
                    var type = well.UsesTypeOverride ? well.TypeOverride
                        : well.IsAttachableWell ? attachable.MagazineType : firearm.MagazineType;
                    if (SelectionPolicy.MagazineFits((int)magazine.MagazineType, magazine.IsIntegrated,
                        magazine.IsBeltBox, (int)type, well.IsAttachableWell || well.UsesSecondaryMagSlots,
                        well.IsBeltBox, firearm != null && firearm.HasBelt)) return true;
                }
            }

            var clip = candidate as FVRFireArmClip;
            if (clip != null)
            {
                if (candidate.GetComponentInChildren<FVRFireArmClipTriggerClip>(true) == null) return false;
                foreach (var well in target.GetComponentsInChildren<FVRFireArmClipTriggerWell>(true))
                    if (BelongsTo(well.transform, target) && well.FireArm != null
                        && clip.ClipType != 0 && well.FireArm.ClipType == clip.ClipType) return true;
            }

            var loader = candidate as Speedloader;
            if (loader != null)
            {
                // Caliber alone permits partial/incorrect cylinder layouts. Require explicit authored
                // compatibility for speedloaders, including special shotgun/launcher devices.
                foreach (var obj in Objects(target))
                    if (Contains(obj.ObjectWrapper == null ? null : obj.ObjectWrapper.CompatibleSpeedLoaders,
                        candidate.ObjectWrapper) && LoaderRoundMatches(loader, obj)) return true;
            }
            return false;
        }

        private static bool LoaderRoundMatches(Speedloader loader, FVRPhysicalObject target)
        {
            if (loader.Chambers == null || loader.Chambers.Count == 0) return false;
            var firearm = target as FVRFireArm;
            if (firearm == null) return false;
            foreach (var chamber in loader.Chambers)
                if (chamber == null || chamber.Type != firearm.RoundType) return false;
            return true;
        }

        private static bool Contains(List<FVRObject> objects, FVRObject item)
        {
            if (objects == null || item == null) return false;
            foreach (var obj in objects)
                if (obj != null && (obj == item || obj.ItemID == item.ItemID)) return true;
            return false;
        }

        // Traverse registered attachments as well as transforms: installed adapters do not always
        // parent to their immediate owning rail. Never infer an adapter that is not installed.
        private static IEnumerable<FVRPhysicalObject> Objects(FVRPhysicalObject root)
        {
            var queue = new Queue<FVRPhysicalObject>();
            var seen = new HashSet<FVRPhysicalObject>();
            queue.Enqueue(root);
            while (queue.Count != 0)
            {
                var obj = queue.Dequeue();
                if (obj == null || !seen.Add(obj)) continue;
                yield return obj;
                if (obj.Attachments != null)
                    foreach (var attachment in obj.Attachments) if (attachment != null) queue.Enqueue(attachment);
                if (obj.AttachmentMounts != null)
                    foreach (var mount in obj.AttachmentMounts)
                        if (mount != null && mount.AttachmentsList != null)
                            foreach (var attachment in mount.AttachmentsList)
                                if (attachment != null) queue.Enqueue(attachment);
            }
        }

        private static IEnumerable<FVRFireArmAttachmentMount> Mounts(FVRPhysicalObject root)
        {
            var queue = new Queue<FVRFireArmAttachmentMount>();
            var seen = new HashSet<FVRFireArmAttachmentMount>();
            foreach (var obj in Objects(root))
            {
                if (obj.AttachmentMounts != null)
                    foreach (var mount in obj.AttachmentMounts) queue.Enqueue(mount);
                foreach (var mount in obj.GetComponentsInChildren<FVRFireArmAttachmentMount>(true))
                    if (BelongsTo(mount.transform, root)) queue.Enqueue(mount);
            }
            while (queue.Count != 0)
            {
                var mount = queue.Dequeue();
                if (mount == null || !seen.Add(mount)) continue;
                if (mount.SubMounts != null)
                    foreach (var child in mount.SubMounts) queue.Enqueue(child);
                if (mount.AttachmentsList != null)
                    foreach (var child in mount.AttachmentsList)
                        if (child != null && child.AttachmentMounts != null)
                            foreach (var extra in child.AttachmentMounts) queue.Enqueue(extra);
                // A hidden/disabled connector cannot currently receive an attachment.
                var collider = mount.GetComponent<Collider>();
                if (!mount.enabled || collider == null || !collider.enabled) continue;
                if (!ActiveToRoot(mount.transform, root.transform)) continue;
                yield return mount;
            }
        }

        private static bool ActiveToRoot(Transform current, Transform root)
        {
            while (current != null)
            {
                if (!current.gameObject.activeSelf) return false;
                if (current == root) return true;
                current = current.parent;
            }
            return false;
        }

        private static bool BelongsTo(Transform current, FVRPhysicalObject root)
        {
            // Descendant prefab furniture is not necessarily installed. Require either the root
            // or an attachment whose native root resolves back to this held item.
            var owner = current.GetComponentInParent<FVRPhysicalObject>();
            if (owner == root) return true;
            var attachment = owner as FVRFireArmAttachment;
            return attachment != null && attachment.curMount != null && attachment.GetRootObject() == root;
        }
    }
}
