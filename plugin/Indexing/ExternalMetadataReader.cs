using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Gundomizer.Indexing
{
    // Unity 5's collector stops all managed threads. Keep the allocation-heavy decoder in
    // a separate CLR process; only small requests and checksummed connector records cross over.
    internal sealed class ExternalMetadataReader
    {
        private readonly string executable, directory, game, plugins;
        private readonly Dictionary<string, ConnectorKind> types;
        private readonly ReadBudget budget;
        internal ExternalMetadataReader(string executable, string directory, string game, string plugins, Dictionary<string, ConnectorKind> types, ReadBudget budget)
        { this.executable = executable; this.directory = directory; this.game = game; this.plugins = plugins; this.types = types; this.budget = budget; }

        internal BundleFacts Read(string bundle, HashSet<string> targets)
        {
            if (!File.Exists(executable)) throw new IOException("Metadata reader executable is missing; keeping live checks");
            string jobDirectory = Path.Combine(directory, "reader-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(jobDirectory);
            string request = Path.Combine(jobDirectory, "request.bin"), output = Path.Combine(jobDirectory, "result.gidx");
            string stats = output + ".stats", error = output + ".error";
            Process process = null;
            try
            {
                ReaderProtocol.WriteJob(request, bundle, directory, game, plugins, types, targets);
                using (var parent = Process.GetCurrentProcess())
                {
                    // All variable paths are generated locally; Windows filenames cannot contain quotes.
                    process = Process.Start(new ProcessStartInfo(executable,
                        "\"" + request + "\" \"" + output + "\" " + parent.Id)
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                        WorkingDirectory = Path.GetDirectoryName(executable) });
                }
                if (process == null) throw new IOException("Could not start metadata reader");
                var watch = Stopwatch.StartNew();
                while (!process.WaitForExit(50))
                {
                    budget.Check();
                    if (watch.Elapsed.TotalMinutes > 5) throw new IOException("Metadata reader exceeded its time budget");
                }
                if (process.ExitCode != 0)
                {
                    string detail = File.Exists(error) && new FileInfo(error).Length < 8192 ? File.ReadAllText(error) : "exit " + process.ExitCode;
                    throw new IOException("Metadata reader: " + detail);
                }
                var result = IndexCache.Load(output, ReaderProtocol.Version);
                if (result == null) throw new IOException("Metadata reader returned an invalid result");
                using (var reader = new BinaryReader(File.OpenRead(stats)))
                { result.BytesRead = reader.ReadInt64(); result.CacheHit = reader.ReadBoolean(); result.CacheWarning = ReaderProtocol.ReadString(reader); }
                return result;
            }
            finally
            {
                if (process != null)
                {
                    try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
                    process.Dispose();
                }
                // Only this job's named files are removed; no recursive cleanup of user paths.
                foreach (string file in new[] { request, output, stats, error })
                    try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { }
                try { Directory.Delete(jobDirectory, false); } catch (IOException) { }
            }
        }
    }
}
