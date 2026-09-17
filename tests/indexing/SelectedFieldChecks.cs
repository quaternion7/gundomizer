using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Gundomizer.Indexing;

internal static class SelectedFieldChecks
{
    private static AssetTypeTemplateField Field(string name, AssetValueType type, params AssetTypeTemplateField[] children)
        => new AssetTypeTemplateField { Name = name, ValueType = type, Children = new List<AssetTypeTemplateField>(children) };
    private static void Require(bool value, string message) { if (!value) throw new Exception("FAIL " + message); }
    internal static void Run()
    {
        BooleanChecks();
        var pointer = Field("m_Script", AssetValueType.None, Field("m_FileID", AssetValueType.Int32), Field("m_PathID", AssetValueType.Int64));
        var array = Field("unrelated", AssetValueType.Array, Field("size", AssetValueType.Int32), pointer); array.IsArray = true;
        var aligned = Field("flag", AssetValueType.Bool); aligned.IsAligned = true;
        var template = Field("Base", AssetValueType.None, Field("name", AssetValueType.String), aligned, array,
            pointer, Field("MagazineType", AssetValueType.Int32), Field("IsIntegrated", AssetValueType.Bool), Field("tail", AssetValueType.Int64));
        var memory = new MemoryStream(); var writer = new BinaryWriter(memory);
        writer.Write(3); writer.Write(new byte[] { 65, 66, 67, 0 });
        writer.Write(new byte[] { 1, 0, 0, 0 });
        writer.Write(2); writer.Write(0); writer.Write(23L); writer.Write(0); writer.Write(45L);
        writer.Write(0); writer.Write(67L); writer.Write(8); writer.Write(true);
        long expectedEnd = memory.Position;
        writer.Write(99L); writer.Flush(); memory.Position = 0;
        var budget = new ReadBudget(new ManualResetEvent(false), false);
        var reader = new AssetsFileReader(memory);
        var result = SelectedFields.Read(template, (int)AssetClassID.MonoBehaviour, reader, memory.Length, budget);
        Require(result.Children.Count == 3 && result["name"].IsDummy && result["unrelated"].IsDummy,
            "unrelated strings, aligned values and arrays are not materialized");
        Require(result["m_Script"]["m_PathID"].AsLong == 67 && result["MagazineType"].AsInt == 8
            && result["IsIntegrated"].AsBool && reader.Position == expectedEnd, "selected fields retain values and stop before unused tail");
        reader.Position = 0; var reference = template.MakeValue(reader);
        Require(reference["m_Script"]["m_PathID"].AsLong == result["m_Script"]["m_PathID"].AsLong
            && reference["MagazineType"].AsInt == result["MagazineType"].AsInt
            && reference["IsIntegrated"].AsBool == result["IsIntegrated"].AsBool, "selected values agree with full library deserialization");
        foreach (int count in new[] { -1, int.MaxValue })
        {
            memory.Position = 0; writer.Write(count); writer.Flush(); memory.Position = 0;
            bool rejected = false;
            try { SelectedFields.Read(template, (int)AssetClassID.MonoBehaviour, reader, memory.Length, budget); }
            catch (NotSupportedException) { rejected = true; }
            Require(rejected, "invalid string count fails before allocation");
        }
        var componentArray = Field("m_Component", AssetValueType.Array, Field("size", AssetValueType.Int32), pointer);
        componentArray.IsArray = true;
        template = Field("Base", AssetValueType.None, componentArray);
        memory.Position = 0; writer.Write(3); writer.Flush(); memory.Position = 0;
        bool truncated = false;
        try { SelectedFields.Read(template, (int)AssetClassID.GameObject, reader, 8, budget); }
        catch (EndOfStreamException) { truncated = true; }
        Require(truncated, "array count cannot read past the declared object record");
        memory.Position = 0; writer.Write(0); writer.Flush(); memory.Position = 0;
        result = SelectedFields.Read(template, (int)AssetClassID.GameObject, reader, 4, budget);
        Require(result["m_Component"].Children.Count == 0, "empty selected array remains valid");
        Console.WriteLine("PASS selected fields: full-reader agreement, alignment, strings, arrays, early stop and malformed bounds");
    }

    private static void BooleanChecks()
    {
        foreach (var type in new[] { AssetValueType.Bool, AssetValueType.UInt8 })
        foreach (byte raw in new byte[] { 0, 1 })
        {
            var field = Field("IsIntegrated", type).MakeValue(new AssetsFileReader(new MemoryStream(new[] { raw })));
            bool value;
            Require(BundleMetadataReader.TryBoolean(field, out value) && value == (raw == 1),
                "integrated magazine flag accepts bool and canonical Unity byte representations");
        }
        bool ignored;
        Require(!BundleMetadataReader.TryBoolean(null, out ignored), "missing integrated flag is unknown, not false");
        var invalid = Field("IsIntegrated", AssetValueType.UInt8).MakeValue(new AssetsFileReader(new MemoryStream(new byte[] { 2 })));
        Require(!BundleMetadataReader.TryBoolean(invalid, out ignored), "noncanonical byte flag is not trusted");
        var integer = Field("IsIntegrated", AssetValueType.Int32).MakeValue(new AssetsFileReader(new MemoryStream(new byte[] { 1, 0, 0, 0 })));
        Require(!BundleMetadataReader.TryBoolean(integer, out ignored), "unfamiliar numeric flag layout remains unknown");
        Console.WriteLine("PASS serialized magazine flags: bool and byte 0/1 accepted; missing, noncanonical and unrelated numeric types rejected");
    }
}
