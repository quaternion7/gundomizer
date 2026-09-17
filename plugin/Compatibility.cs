using System;
using System.Collections.Generic;
using System.Text;
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

        private static CompatibilityKind Kind(FVRObject candidate)
        {
            if (candidate == null) return CompatibilityKind.Unsupported;
            switch (candidate.Category)
            {
                case FVRObject.ObjectCategory.Firearm: return CompatibilityKind.Firearm;
                case FVRObject.ObjectCategory.Attachment: return CompatibilityKind.Attachment;
                case FVRObject.ObjectCategory.Magazine: return CompatibilityKind.Magazine;
                case FVRObject.ObjectCategory.Clip: return CompatibilityKind.Clip;
                case FVRObject.ObjectCategory.SpeedLoader: return CompatibilityKind.Speedloader;
                default: return CompatibilityKind.Unsupported;
            }
        }

        internal static Query Capture(FVRPhysicalObject held) => new Query(held);
        internal static Query CaptureFiltered(FVRPhysicalObject held, CompatibleSelection selection) => new Query(held, selection);

        // Commit uses a fresh query: a snapshot is never a cached guarantee about changing mounts.
        internal static bool Matches(FVRPhysicalObject held, GameObject candidate)
            => held != null && candidate != null && Capture(held).Matches(candidate);

        internal sealed class Query
        {
            private readonly FVRPhysicalObject target;
            private readonly List<FVRPhysicalObject> objects;
            private readonly List<FVRFireArmAttachmentMount> mounts;
            private readonly List<FVRFireArmReloadTriggerWell> magazineWells = new List<FVRFireArmReloadTriggerWell>();
            private readonly List<FVRFireArmClipTriggerWell> clipWells = new List<FVRFireArmClipTriggerWell>();
            private readonly CompatibilityRequirements requirements = new CompatibilityRequirements();
            private readonly HashSet<int> mountTypes = new HashSet<int>();
            private readonly CompatibleSelection selection;
            internal readonly string Signature;

            internal Query(FVRPhysicalObject held, CompatibleSelection selection = null)
            {
                this.selection = selection;
                if (held == null) throw new ArgumentNullException(nameof(held));
                target = held;
                // Traverse the held assembly once per roll, not again for every candidate prefab.
                objects = new List<FVRPhysicalObject>(Objects(held));
                mounts = new List<FVRFireArmAttachmentMount>(Mounts(held, objects));
                requirements.HasMount = mounts.Count > 0;
                foreach (var well in held.GetComponentsInChildren<FVRFireArmReloadTriggerWell>(true))
                {
                    if (well == null || !BelongsTo(well.transform, held)) continue;
                    magazineWells.Add(well);
                    if (well.UsesTypeOverride) requirements.MagazineTypes.Add((int)well.TypeOverride);
                    else if (well.IsAttachableWell && well.AFireArm != null) requirements.MagazineTypes.Add((int)well.AFireArm.MagazineType);
                    else if (well.FireArm != null) requirements.MagazineTypes.Add((int)well.FireArm.MagazineType);
                }
                foreach (var well in held.GetComponentsInChildren<FVRFireArmClipTriggerWell>(true))
                {
                    if (well == null || !BelongsTo(well.transform, held)) continue;
                    clipWells.Add(well);
                    if (well.FireArm != null) requirements.ClipTypes.Add((int)well.FireArm.ClipType);
                }
                foreach (var obj in objects)
                    if (obj.ObjectWrapper != null && obj.ObjectWrapper.CompatibleSpeedLoaders != null)
                        foreach (var loader in obj.ObjectWrapper.CompatibleSpeedLoaders)
                            if (loader != null) requirements.SpeedloaderIds.Add(loader.ItemID);
                requirements.CanMatchFirearm = held is FVRFireArmMagazine || held is FVRFireArmClip
                    || held is Speedloader || held is FVRFireArmAttachment;
                var signature = new StringBuilder();
                foreach (var obj in objects) signature.Append(obj.GetInstanceID()).Append(',');
                foreach (var mount in mounts)
                {
                    mountTypes.Add((int)mount.Type);
                    signature.Append('|').Append(mount.GetInstanceID()).Append(':').Append((int)mount.Type)
                        .Append(':').Append(mount.AttachmentsList == null ? -1 : mount.AttachmentsList.Count);
                }
                foreach (var type in requirements.MagazineTypes) signature.Append("m").Append(type);
                foreach (var type in requirements.ClipTypes) signature.Append("c").Append(type);
                foreach (var id in requirements.SpeedloaderIds) signature.Append("s").Append(id).Append(';');
                Signature = signature.ToString();
            }

            internal List<CompatibilityKind> AvailableKinds()
            {
                var result = new List<CompatibilityKind>();
                if (requirements.MagazineTypes.Count > 0) result.Add(CompatibilityKind.Magazine);
                if (mountTypes.Count > 0) result.Add(CompatibilityKind.Attachment);
                if (requirements.ClipTypes.Count > 0) result.Add(CompatibilityKind.Clip);
                if (requirements.SpeedloaderIds.Count > 0) result.Add(CompatibilityKind.Speedloader);
                if (requirements.CanMatchFirearm) result.Add(CompatibilityKind.Firearm);
                return result;
            }

            internal List<int> Connectors(CompatibilityKind kind)
            {
                var result = new List<int>(kind == CompatibilityKind.Attachment ? mountTypes
                    : kind == CompatibilityKind.Magazine ? requirements.MagazineTypes
                    : kind == CompatibilityKind.Clip ? requirements.ClipTypes : new HashSet<int>());
                result.Sort();
                return result;
            }

            private bool Included(CompatibilityKind kind, int? connector = null)
            {
                if (selection == null) return true;
                if (!selection.Includes(kind, connector)) return false;
                if (connector.HasValue) return true;
                // Even an unknown candidate cannot use a connector family the user entirely
                // disabled. Avoid speculative attachment loads during a magazine-only roll.
                var types = kind == CompatibilityKind.Attachment ? mountTypes : kind == CompatibilityKind.Magazine
                    ? requirements.MagazineTypes : kind == CompatibilityKind.Clip ? requirements.ClipTypes : null;
                if (types == null) return true;
                foreach (int type in types) if (selection.Includes(kind, type)) return true;
                return false;
            }

            internal void Prefilter(List<ItemSpawnerID> candidates)
            {
                int write = 0;
                for (int i = 0; i < candidates.Count; ++i)
                    if (CouldMatch(candidates[i], true)) candidates[write++] = candidates[i];
                candidates.RemoveRange(write, candidates.Count - write);
            }

            internal bool CouldMatch(ItemSpawnerID entry, bool useIndex)
            {
                var obj = entry == null ? null : entry.MainObject;
                if (obj == null) return false;
                var indexed = useIndex ? ConnectorIndex.Find(obj) : null;
                var attachment = indexed as FVRFireArmAttachment;
                if (attachment != null) return Included(CompatibilityKind.Attachment, (int)attachment.Type) && mountTypes.Contains((int)attachment.Type);
                var magazine = indexed as FVRFireArmMagazine;
                if (magazine != null) return Included(CompatibilityKind.Magazine, (int)magazine.MagazineType) && !magazine.IsIntegrated && requirements.MagazineTypes.Contains((int)magazine.MagazineType);
                var clip = indexed as FVRFireArmClip;
                if (clip != null) return Included(CompatibilityKind.Clip, (int)clip.ClipType) && requirements.ClipTypes.Contains((int)clip.ClipType);
                if (indexed is FVRFireArm) return Included(CompatibilityKind.Firearm) && requirements.CanMatchFirearm;
                var persisted = useIndex && indexed == null ? PersistentConnectorIndex.Find(obj) : null;
                if (persisted != null)
                {
                    if (persisted.Kind == Indexing.ConnectorKind.Attachment) return Included(CompatibilityKind.Attachment, persisted.Connector) && mountTypes.Contains(persisted.Connector);
                    if (persisted.Kind == Indexing.ConnectorKind.Magazine)
                        return Included(CompatibilityKind.Magazine, persisted.Connector) && !persisted.Integrated && requirements.MagazineTypes.Contains(persisted.Connector);
                    if (persisted.Kind == Indexing.ConnectorKind.Clip) return Included(CompatibilityKind.Clip, persisted.Connector) && requirements.ClipTypes.Contains(persisted.Connector);
                }
                var kind = Kind(obj);
                int? connector = kind == CompatibilityKind.Magazine && (int)obj.MagazineType != 0 ? (int?)obj.MagazineType
                    : kind == CompatibilityKind.Clip && (int)obj.ClipType != 0 ? (int?)obj.ClipType : null;
                return Included(kind, connector) && requirements.CouldMatch(kind, (int)obj.MagazineType, (int)obj.ClipType, obj.ItemID);
            }

            internal bool Matches(GameObject candidate)
            {
                if (target == null || candidate == null) return false;
                var item = candidate.GetComponent<FVRPhysicalObject>();
                if (item == null) return false;
                if (FitsOnto(item)) return true;
                // Reverse matching must inspect the candidate firearm's own wells and mounts.
                return Included(CompatibilityKind.Firearm) && requirements.CanMatchFirearm && item is FVRFireArm && Capture(item).FitsOnto(target);
            }

            private bool FitsOnto(FVRPhysicalObject candidate)
            {
                var attachment = candidate as FVRFireArmAttachment;
                if (attachment != null && Included(CompatibilityKind.Attachment, (int)attachment.Type))
                {
                    if (!attachment.CanAttach()) return false;
                    foreach (var mount in mounts)
                        if (mount != null && mount.Type == attachment.Type && mount.isMountableOn(attachment)) return true;
                }

                var magazine = candidate as FVRFireArmMagazine;
                if (magazine != null && Included(CompatibilityKind.Magazine, (int)magazine.MagazineType))
                {
                    // Native connector/override/belt-box rules; an occupied well permits a spare.
                    if (candidate.GetComponentInChildren<FVRFireArmReloadTriggerMag>(true) == null) return false;
                    foreach (var well in magazineWells)
                    {
                        if (well == null) continue;
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
                if (clip != null && Included(CompatibilityKind.Clip, (int)clip.ClipType))
                {
                    if (candidate.GetComponentInChildren<FVRFireArmClipTriggerClip>(true) == null) return false;
                    foreach (var well in clipWells)
                        if (well != null && well.FireArm != null && clip.ClipType != 0 && well.FireArm.ClipType == clip.ClipType) return true;
                }

                var loader = candidate as Speedloader;
                if (loader != null && Included(CompatibilityKind.Speedloader))
                {
                    // Preserve authored speedloader compatibility, including exotic devices.
                    foreach (var obj in objects)
                        if (obj != null && Contains(obj.ObjectWrapper == null ? null : obj.ObjectWrapper.CompatibleSpeedLoaders,
                            candidate.ObjectWrapper) && LoaderRoundMatches(loader, obj)) return true;
                }
                return false;
            }
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
        internal static IEnumerable<FVRPhysicalObject> Objects(FVRPhysicalObject root)
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

        private static IEnumerable<FVRFireArmAttachmentMount> Mounts(FVRPhysicalObject root, IEnumerable<FVRPhysicalObject> objects)
        {
            var queue = new Queue<FVRFireArmAttachmentMount>();
            var seen = new HashSet<FVRFireArmAttachmentMount>();
            foreach (var obj in objects)
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
