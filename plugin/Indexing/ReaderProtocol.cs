using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Gundomizer.Indexing
{
    internal static class ReaderProtocol
    {
        internal const string Version = "gundomizer-reader-2";
        internal static void WriteString(BinaryWriter writer, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length > 8192) throw new IOException("Reader string exceeds limit");
            writer.Write(bytes.Length); writer.Write(bytes);
        }
        internal static string ReadString(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 8192) throw new IOException("Invalid reader string");
            byte[] data = reader.ReadBytes(count);
            if (data.Length != count) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(data);
        }
        internal static int Count(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 32768) throw new IOException("Reader collection exceeds limit");
            return count;
        }
        internal static void WriteJob(string file, string bundle, string cache, string game, string plugins,
            Dictionary<string, ConnectorKind> types, HashSet<string> targets)
        {
            using (var writer = new BinaryWriter(File.Create(file)))
            {
                WriteString(writer, Version); WriteString(writer, bundle);
                WriteString(writer, cache); WriteString(writer, game); WriteString(writer, plugins);
                writer.Write(types.Count);
                foreach (var pair in types) { WriteString(writer, pair.Key); writer.Write((byte)pair.Value); }
                writer.Write(targets == null);
                if (targets != null) { writer.Write(targets.Count); foreach (string target in targets) WriteString(writer, target); }
            }
        }
    }
}
