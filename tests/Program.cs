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
