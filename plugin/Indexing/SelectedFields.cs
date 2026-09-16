using System;
using System.Collections.Generic;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace Gundomizer.Indexing
{
    internal static class SelectedFields
    {
        private static readonly string[] Container = { "m_Container" };
        private static readonly string[] Components = { "m_Component" };
        private static readonly string[] Script = { "m_AssemblyName", "m_Namespace", "m_ClassName" };
        private static readonly string[] Connector = { "m_Script", "Type", "MagazineType", "ClipType", "IsIntegrated" };

        internal static AssetTypeValueField Read(AssetsManager manager, AssetsFileInstance file, AssetFileInfo info, ReadBudget budget)
        {
            var template = manager.GetTemplateBaseField(file, info);
            if (template == null) throw new NotSupportedException("Missing serialized type tree");
            var reader = file.file.Reader;
            reader.Position = info.GetAbsoluteByteOffset(file.file);
            return Read(template, info.TypeId, reader, info.ByteSize, budget);
        }
        internal static AssetTypeValueField Read(AssetTypeTemplateField template, int typeId, AssetsFileReader reader, long size, ReadBudget budget)
        {
            var names = typeId == (int)AssetClassID.AssetBundle ? Container
                : typeId == (int)AssetClassID.GameObject ? Components
                : typeId == (int)AssetClassID.MonoScript ? Script : Connector;
            var result = new AssetTypeValueField { TemplateField = template, Children = new List<AssetTypeValueField>(names.Length) };
            int remaining = 0;
            foreach (var field in template.Children) if (Array.IndexOf(names, field.Name) >= 0) ++remaining;
            long end = checked(reader.Position + size);
            foreach (var field in template.Children)
            {
                if (remaining == 0) break; // No reason to parse the rest of a component's unrelated fields.
                budget.Check();
                long start = reader.Position;
                Skip(field, reader, end, budget, 0);
                if (Array.IndexOf(names, field.Name) < 0) continue;
                long next = reader.Position;
                reader.Position = start;
                result.Children.Add(field.MakeValue(reader));
                if (reader.Position != next) throw new NotSupportedException("Unexpected serialized field layout");
                --remaining;
            }
            return result;
        }

        // Walk past unrelated values without constructing their object trees. Validate the
        // selected values the same way before allowing the library to allocate their arrays.
        private static void Skip(AssetTypeTemplateField field, AssetsFileReader reader, long end, ReadBudget budget, int depth)
        {
            budget.Check();
            if (depth > 64) throw new NotSupportedException("Oversized serialized nesting");
            if (field.IsArray)
            {
                if (field.Children.Count != 2 || (field.Children[0].ValueType != AssetValueType.Int32
                    && field.Children[0].ValueType != AssetValueType.UInt32)) throw new NotSupportedException("Unknown array layout");
                int count = Count(reader, end);
                if (field.ValueType == AssetValueType.ByteArray) Advance(reader, end, count);
                else
                {
                    long stride = FixedSize(field.Children[1], depth + 1);
                    if (stride >= 0) Advance(reader, end, checked(stride * count));
                    else for (int i = 0; i < count; ++i) Skip(field.Children[1], reader, end, budget, depth + 1);
                }
            }
            else if (field.ValueType == AssetValueType.String)
            {
                Advance(reader, end, Count(reader, end));
                Align(reader, end); // Serialized strings always align, regardless of their template flag.
                return;
            }
            else if (field.ValueType == AssetValueType.None)
            {
                foreach (var child in field.Children) Skip(child, reader, end, budget, depth + 1);
            }
            else
            {
                if (field.Children.Count != 0) throw new NotSupportedException("Unknown primitive layout");
                int size;
                switch (field.ValueType)
                {
                    case AssetValueType.Bool: case AssetValueType.Int8: case AssetValueType.UInt8: size = 1; break;
                    case AssetValueType.Int16: case AssetValueType.UInt16: size = 2; break;
                    case AssetValueType.Int32: case AssetValueType.UInt32: case AssetValueType.Float: size = 4; break;
                    case AssetValueType.Int64: case AssetValueType.UInt64: case AssetValueType.Double: size = 8; break;
                    default: throw new NotSupportedException("Unsupported serialized field");
                }
                Advance(reader, end, size);
            }
            if (field.IsAligned) Align(reader, end);
        }
        private static int Count(AssetsFileReader reader, long end)
        {
            if (end - reader.Position < 4) throw new EndOfStreamException();
            int count = reader.ReadInt32();
            if (count < 0 || count > 1048576 || count > end - reader.Position)
                throw new NotSupportedException("Oversized serialized collection");
            return count;
        }
        private static void Advance(AssetsFileReader reader, long end, long size)
        {
            if (size < 0 || size > end - reader.Position) throw new EndOfStreamException();
            reader.Position += size;
        }
        private static void Align(AssetsFileReader reader, long end) => Advance(reader, end, (4 - reader.Position % 4) % 4);
        private static long FixedSize(AssetTypeTemplateField field, int depth)
        {
            if (depth > 64 || field.IsArray || field.IsAligned) return -1;
            switch (field.ValueType)
            {
                case AssetValueType.Bool: case AssetValueType.Int8: case AssetValueType.UInt8: return field.Children.Count == 0 ? 1 : -1;
                case AssetValueType.Int16: case AssetValueType.UInt16: return field.Children.Count == 0 ? 2 : -1;
                case AssetValueType.Int32: case AssetValueType.UInt32: case AssetValueType.Float: return field.Children.Count == 0 ? 4 : -1;
                case AssetValueType.Int64: case AssetValueType.UInt64: case AssetValueType.Double: return field.Children.Count == 0 ? 8 : -1;
                case AssetValueType.None:
                    long total = 0;
                    foreach (var child in field.Children) { long size = FixedSize(child, depth + 1); if (size < 0) return -1; total = checked(total + size); }
                    return total;
                default: return -1;
            }
        }
    }
}
