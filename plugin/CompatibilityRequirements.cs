using System;
using System.Collections.Generic;

namespace Gundomizer
{
    internal enum CompatibilityKind { Unsupported, Firearm, Attachment, Magazine, Clip, Speedloader }

    // Lightweight facts from the held object. Unknown candidate metadata must reach native checks.
    internal sealed class CompatibilityRequirements
    {
        internal readonly HashSet<int> MagazineTypes = new HashSet<int>();
        internal readonly HashSet<int> ClipTypes = new HashSet<int>();
        internal readonly HashSet<string> SpeedloaderIds = new HashSet<string>(StringComparer.Ordinal);
        internal bool HasMount;
        internal bool CanMatchFirearm;

        internal bool CouldMatch(CompatibilityKind kind, int magazineType, int clipType, string itemId)
        {
            switch (kind)
            {
                case CompatibilityKind.Attachment: return HasMount;
                case CompatibilityKind.Magazine:
                    return MagazineTypes.Count > 0 && (magazineType == 0 || MagazineTypes.Contains(magazineType));
                case CompatibilityKind.Clip:
                    return ClipTypes.Count > 0 && (clipType == 0 || ClipTypes.Contains(clipType));
                case CompatibilityKind.Speedloader: return itemId != null && SpeedloaderIds.Contains(itemId);
                case CompatibilityKind.Firearm: return CanMatchFirearm;
                default: return false;
            }
        }
    }
}
