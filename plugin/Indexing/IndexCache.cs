using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Gundomizer.Indexing
{
    internal static class IndexCache
    {
        // Bump when field interpretation, alias resolution, or supported script rules change.
        internal const string Schema = "gundomizer-connectors-1/AssetsTools.NET-3.0.5";
        private const int MaxCacheBytes = 16 * 1024 * 1024;
        internal static string Hash(byte[] data)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
        }
        internal static string Key(string path) => Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant()));

        internal static string Fingerprint(string path, string gameIdentity, string pluginsPath, ReadBudget budget)
        {
            var file = new FileInfo(path);
            var text = new StringBuilder(Schema).Append('|').Append(gameIdentity).Append('|').Append(file.FullName.ToLowerInvariant())
                .Append('|').Append(file.Length).Append('|').Append(file.LastWriteTimeUtc.Ticks).Append('|').Append(file.CreationTimeUtc.Ticks);
            // Includes the UnityFS content/directory header plus distant samples. No whole-bundle hash.
            using (var source = new BudgetStream(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read), budget))
            {
                var sample = new byte[(int)Math.Min(65536, source.Length)];
                foreach (long offset in new[] { 0L, Math.Max(0, source.Length / 2 - sample.Length / 2), Math.Max(0, source.Length - sample.Length) })
                {
                    source.Position = offset;
                    int read = 0;
                    while (read < sample.Length)
                    {
                        int n = source.Read(sample, read, sample.Length - read);
                        if (n == 0) throw new EndOfStreamException();
                        read += n;
                    }
                    text.Append('|').Append(Hash(sample));
                }
            }
            // r2modman packages own a directory below plugins; a manifest edit/version change
            // invalidates just that package. Source stats still catch loose/manual bundle updates.
            string pluginRoot = Path.GetFullPath(pluginsPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (file.FullName.StartsWith(pluginRoot, StringComparison.OrdinalIgnoreCase))
            {
                string relative = file.FullName.Substring(pluginRoot.Length);
                int slash = relative.IndexOf(Path.DirectorySeparatorChar);
                string package = slash < 0 ? pluginRoot : Path.Combine(pluginRoot, relative.Substring(0, slash));
                foreach (string manifest in new[] { "manifest.json", "mm_v2_manifest.json" })
                {
                    string full = Path.Combine(package, manifest);
                    if (File.Exists(full))
                    {
                        if (new FileInfo(full).Length > 1024 * 1024) throw new IOException("Oversized package manifest");
                        text.Append('|').Append(manifest).Append(':').Append(Hash(File.ReadAllBytes(full)));
                    }
                }
                // Include package script changes even if the author forgot to increment the version.
                var scripts = Directory.GetFiles(package, "*.dll", SearchOption.AllDirectories);
                Array.Sort(scripts, StringComparer.OrdinalIgnoreCase);
                foreach (string script in scripts)
                {
                    budget.Check();
                    var info = new FileInfo(script);
                    text.Append('|').Append(info.FullName).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks);
                }
            }
            return Hash(Encoding.UTF8.GetBytes(text.ToString()));
        }

        internal static BundleFacts Load(string path, string fingerprint)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > MaxCacheBytes) return null;
                byte[] data = File.ReadAllBytes(path);
                if (data.Length < 32) return null;
                int payloadLength = data.Length - 32;
                using (var sha = SHA256.Create())
                {
                    byte[] checksum = sha.ComputeHash(data, 0, payloadLength);
                    for (int i = 0; i < checksum.Length; ++i) if (checksum[i] != data[payloadLength + i]) return null;
                }
                using (var stream = new MemoryStream(data, 0, payloadLength))
                using (var reader = new BinaryReader(stream))
                {
                    if (ReadString(reader) != Schema || ReadString(reader) != fingerprint) return null;
                    var result = new BundleFacts { SkipReason = ReadString(reader) };
                    int count = reader.ReadInt32();
                    if (count < 0 || count > 32768) return null;
                    for (int i = 0; i < count; ++i)
                    {
                        string asset = ReadString(reader);
                        byte kind = reader.ReadByte();
                        if (kind > 3) return null;
                        result.Add(asset, kind == 0 ? null : new ConnectorFacts { Kind = (ConnectorKind)kind,
                            Connector = reader.ReadInt32(), Integrated = reader.ReadBoolean() });
                    }
                    if (stream.Position != stream.Length) return null;
                    result.Seal();
                    return result;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            { return null; }
        }

        internal static void Save(string path, string fingerprint, BundleFacts facts)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var memory = new MemoryStream())
                using (var writer = new BinaryWriter(memory))
                {
                    WriteString(writer, Schema); WriteString(writer, fingerprint); WriteString(writer, facts.SkipReason);
                    writer.Write(facts.Assets.Count);
                    foreach (var pair in facts.Assets)
                    {
                        WriteString(writer, pair.Key);
                        writer.Write(pair.Value == null ? (byte)0 : (byte)pair.Value.Kind);
                        if (pair.Value != null) { writer.Write(pair.Value.Connector); writer.Write(pair.Value.Integrated); }
                    }
                    writer.Flush();
                    byte[] payload = memory.ToArray();
                    if (payload.Length > MaxCacheBytes - 32) throw new IOException("Index cache exceeds budget");
                    using (var output = File.Create(temporary))
                    using (var sha = SHA256.Create())
                    {
                        output.Write(payload, 0, payload.Length);
                        byte[] checksum = sha.ComputeHash(payload);
                        output.Write(checksum, 0, checksum.Length);
                    }
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > 8192) throw new IOException("Index string exceeds budget");
            writer.Write(bytes.Length); writer.Write(bytes);
        }
        private static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > 8192) throw new IOException("Invalid index string");
            byte[] data = reader.ReadBytes(length);
            if (data.Length != length) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(data);
        }
    }
}
