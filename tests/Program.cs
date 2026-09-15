using System;
using System.Collections.Generic;
using Gundomizer;

static class Program
{
    static int count;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        ++count;
        Console.WriteLine("PASS " + name);
    }

    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--export-icons")
        {
            var shapes = new Dictionary<string, object>();
            foreach (var pair in new[] { new KeyValuePair<string,IconShape[]>("dice",IconGeometry.Dice),
                new KeyValuePair<string,IconShape[]>("compatible",IconGeometry.GunAndHand) })
            {
                var list = new List<object>();
                foreach (var shape in pair.Value) list.Add(new { points = shape.Points, cutout = shape.Cutout });
                shapes.Add(pair.Key,list);
            }
            System.IO.File.WriteAllText(args[1],System.Text.Json.JsonSerializer.Serialize(shapes));
            return;
        }
        var page = new List<string>();
        for (int i = 0; i < 55; ++i) page.Add("gun-" + i);
        var overview = SelectionPolicy.SectionIds(true, page, new[] { "stale-other-category" });
        Check(overview.Count == 55 && overview.Contains("gun-54") && !overview.Contains("stale-other-category"),
            "category overview includes all pages and ignores stale previous subcategory");
        var section = SelectionPolicy.SectionIds(false, page, new[] { "gun-2", "gun-31", "gun-54" });
        Check(section.Count == 3 && section.Contains("gun-54") && !section.Contains("gun-0"),
            "subcategory uses full native filtered set without adding other subcategories");
        Check(SelectionPolicy.SectionIds(false, page, Array.Empty<string>()).Count == 0,
            "empty subcategory never falls back to unfiltered category");
        var unique = SelectionPolicy.SectionIds(true, new[] { "a", "a", "", null, "b" }, null);
        Check(unique.Count == 2, "duplicate registry entries do not weight the roll");
        page.Clear();
        Check(overview.Count == 55, "captured section is isolated from subsequent registry mutation");
        var tags = new Dictionary<int, List<string>> { { 1, new List<string> { "Pistol", "Revolver" } }, { 2, new List<string> { "Modern" } } };
        var snapshot = new TagSelectionSnapshot<int>(tags);
        tags[1].Reverse();
        Check(snapshot.Matches(tags), "reordering selected tags does not cancel a pending roll");
        tags[1][0] = "Rifle";
        Check(!snapshot.Matches(tags), "changing a native tag invalidates the captured roll without mutating its snapshot");
        tags[1] = new List<string> { "Pistol", "Revolver" };
        tags[3] = tags[2]; tags.Remove(2);
        Check(!snapshot.Matches(tags), "the same tag in a different filter category is a context change");
        Check(new TagSelectionSnapshot<int>(null).Matches(new Dictionary<int, List<string>>()),
            "an absent tag selection equals an empty selection");
        tags.Clear(); tags[1] = new List<string>();
        Check(!new TagSelectionSnapshot<int>(tags).Matches(null),
            "native empty filter groups are preserved rather than silently broadening a query");
        var ammo = new AmmoSelection();
        string fmj9 = AmmoSelection.Key(9, 1), fmj45 = AmmoSelection.Key(45, 1), tracer9 = AmmoSelection.Key(9, 2);
        Check(ammo.Includes(fmj9) && ammo.Includes(tracer9), "ammo variants default to included");
        ammo.Set(fmj9, false);
        Check(!ammo.Includes(fmj9) && ammo.Includes(fmj45) && ammo.Includes(tracer9), "ammo exclusions affect only their caliber and variant");
        int revision = ammo.Revision;
        ammo.Set(fmj9, false);
        Check(ammo.Revision == revision, "unchanged ammo choices do not invalidate a roll");
        ammo.Set(tracer9, false);
        Check(!ammo.Includes(fmj9) && !ammo.Includes(tracer9) && ammo.Revision != revision, "disabling all variants preserves an empty pool");
        ammo.Set(fmj9, true);
        Check(ammo.Includes(fmj9) && !ammo.Includes(tracer9), "re-enabling a variant does not reset other choices");
        var perm = new List<string>(overview);
        SelectionPolicy.Shuffle(perm, new Random(42));
        Check(new HashSet<string>(perm).SetEquals(overview) && perm.Count == overview.Count,
            "lazy random traversal visits each candidate exactly once");
        SelectionPolicy.Shuffle(new List<string>(), new Random(1));
        Check(true, "empty pool is safe");
        Check(SelectionPolicy.MagazineFits(7, false, false, 7, false, false, false), "matching ordinary magazine well");
        Check(!SelectionPolicy.MagazineFits(8, false, false, 7, false, false, false), "wrong connector rejected");
        Check(!SelectionPolicy.MagazineFits(0, false, false, 0, false, false, false), "unset connector is not compatibility");
        Check(!SelectionPolicy.MagazineFits(7, true, false, 7, false, false, false), "integrated magazine rejected");
        Check(!SelectionPolicy.MagazineFits(7, false, true, 7, false, false, false), "belt box rejected by ordinary well");
        Check(SelectionPolicy.MagazineFits(7, false, true, 7, false, true, true), "belt box accepted by matching belt well");
        Check(!SelectionPolicy.MagazineFits(7, false, false, 7, false, false, true), "ordinary magazine blocked by current belt");
        Check(SelectionPolicy.MagazineFits(7, false, false, 7, true, true, true), "native secondary and attachable well branch honored");
        var requirements = new CompatibilityRequirements();
        Check(!requirements.CouldMatch(CompatibilityKind.Magazine, 0, 0, "mag") &&
            !requirements.CouldMatch(CompatibilityKind.Clip, 0, 0, "clip"),
            "missing held wells reject even unknown candidate connector metadata");
        requirements.MagazineTypes.Add(7);
        requirements.MagazineTypes.Add(9); // An installed secondary well.
        Check(requirements.CouldMatch(CompatibilityKind.Magazine, 7, 0, "primary") &&
            requirements.CouldMatch(CompatibilityKind.Magazine, 9, 0, "secondary") &&
            !requirements.CouldMatch(CompatibilityKind.Magazine, 8, 0, "wrong"),
            "metadata shortlist includes primary and secondary connectors without broadening to unrelated magazines");
        Check(requirements.CouldMatch(CompatibilityKind.Magazine, 0, 0, "mod-without-tag"),
            "unknown magazine metadata is retained for actual prefab validation");
        requirements.ClipTypes.Add(4);
        Check(requirements.CouldMatch(CompatibilityKind.Clip, 0, 4, "clip") &&
            requirements.CouldMatch(CompatibilityKind.Clip, 0, 0, "unknown") &&
            !requirements.CouldMatch(CompatibilityKind.Clip, 0, 5, "wrong"),
            "clip metadata retains compatible and unknown connectors only");
        requirements.SpeedloaderIds.Add("authored-loader");
        Check(requirements.CouldMatch(CompatibilityKind.Speedloader, 0, 0, "authored-loader") &&
            !requirements.CouldMatch(CompatibilityKind.Speedloader, 0, 0, "other-loader") &&
            !requirements.CouldMatch(CompatibilityKind.Speedloader, 0, 0, null),
            "speedloader shortlist uses authored IDs rather than guessing by caliber");
        Check(!requirements.CouldMatch(CompatibilityKind.Attachment, 0, 0, "attachment"),
            "a target without available mounts avoids loading attachment candidates");
        requirements.HasMount = true;
        Check(requirements.CouldMatch(CompatibilityKind.Attachment, 0, 0, "unknown-mount-tag"),
            "available mounts retain attachments for native connector checks without trusting broad display tags");
        Check(!requirements.CouldMatch(CompatibilityKind.Firearm, 0, 0, "gun"),
            "a held firearm does not request unrelated firearm prefab matches");
        requirements.CanMatchFirearm = true;
        Check(requirements.CouldMatch(CompatibilityKind.Firearm, 0, 0, "gun") &&
            !requirements.CouldMatch(CompatibilityKind.Unsupported, 0, 0, "cartridge"),
            "reverse matching permits firearm candidates while deferred categories stay excluded");
        // Regression fixture: shipped HAMScope4x24/RedDotSight/_Interface has an empty Components
        // list and a null UISpawnPoint. An empty list alone is legal; the missing transform is not.
        Check(SpawnSafetyPolicy.ReflexSightProblem(false, true, false)?.Contains("UISpawnPoint") == true,
            "HAM combo scope missing UI transform is rejected before native Awake");
        Check(SpawnSafetyPolicy.ReflexSightProblem(true, true, false) == null,
            "a valid reflex sight with no adjustment controls remains spawnable");
        Check(SpawnSafetyPolicy.ReflexSightProblem(true, false, false) != null &&
            SpawnSafetyPolicy.ReflexSightProblem(true, true, true) != null,
            "null adjustment lists or elements are rejected before native iteration");
        Console.WriteLine(count + " checks passed.");
    }
}
