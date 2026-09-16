using System;
using System.Collections.Generic;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace Gundomizer.Indexing
{
    internal sealed class BundleMetadataReader
    {
        private readonly Dictionary<string, ConnectorKind> knownTypes;
        private readonly ReadBudget budget;
        internal BundleMetadataReader(Dictionary<string, ConnectorKind> knownTypes, ReadBudget budget)
        { this.knownTypes = knownTypes; this.budget = budget; }

        internal BundleFacts Read(string path)
        {
            var result = new BundleFacts();
            var manager = new AssetsManager();
            using (var input = new BudgetStream(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read), budget))
            {
                try
                {
                    // Validate the small header before the library allocates its directory buffers.
                    var headerBytes = new byte[Math.Min(256, (int)Math.Min(input.Length, 256))];
                    int read = input.Read(headerBytes, 0, headerBytes.Length);
                    var header = new AssetBundleHeader();
                    using (var headerStream = new MemoryStream(headerBytes, 0, read))
                        header.Read(new AssetsFileReader(headerStream));
                    if (header.Signature != "UnityFS" || header.Version < 6 || header.Version > 8
                        || header.FileStreamHeader.DecompressedSize > 4 * 1024 * 1024
                        || header.FileStreamHeader.CompressedSize > 4 * 1024 * 1024
                        || header.GetCompressionType() == 1)
                        throw new NotSupportedException("Unsupported or oversized bundle header");
                    input.Position = 0;
                    var bundle = manager.LoadBundleFile(input, path, false);
                    if (bundle.file.DataIsCompressed) throw new NotSupportedException("LZMA data requires full decompression");
                    foreach (var block in bundle.file.BlockAndDirInfo.BlockInfos)
                        if (block.DecompressedSize > 1024 * 1024 || block.GetCompressionType() == 1)
                            throw new NotSupportedException("Oversized or LZMA data block");
                    if (bundle.file.DataReader.BaseStream is LZ4BlockStream blocks) blocks.maxBlockMapSize = 4;
                    if (bundle.file.BlockAndDirInfo.DirectoryInfos.Count > 1024) throw new NotSupportedException("Oversized bundle directory");
                    for (int i = 0; i < bundle.file.BlockAndDirInfo.DirectoryInfos.Count; ++i)
                    {
                        budget.Check();
                        if (!bundle.file.IsAssetsFile(i)) continue;
                        bundle.file.GetFileRange(i, out long offset, out long length);
                        bundle.file.DataReader.Position = offset;
                        var fileHeader = new AssetsFileHeader();
                        fileHeader.Read(bundle.file.DataReader);
                        if (fileHeader.MetadataSize > 16 * 1024 * 1024 || fileHeader.MetadataSize < 0)
                            throw new NotSupportedException("Oversized serialized metadata");
                        var file = manager.LoadAssetsFileFromBundle(bundle, i, false); // Never resolve external dependencies.
                        foreach (var info in file.file.GetAssetsOfType(AssetClassID.AssetBundle))
                        {
                            var container = Field(manager, file, info.PathId, 4 * 1024 * 1024)["m_Container"]["Array"];
                            if (container.IsDummy || container.Children.Count > 32768) continue;
                            foreach (var pair in container.Children)
                            {
                                budget.Check();
                                var pointer = pair["second"]["asset"];
                                string assetPath = pair["first"].AsString;
                                if (assetPath.Length > 2048) continue;
                                ConnectorFacts facts = null;
                                if (Local(pointer)) facts = RootFacts(manager, file, pointer["m_PathID"].AsLong);
                                result.Add(assetPath, facts);
                            }
                        }
                        manager.UnloadAssetsFile(file);
                    }
                    result.Seal();
                    result.BytesRead = input.BytesRead;
                    return result;
                }
                finally { manager.UnloadAll(); }
            }
        }

        private ConnectorFacts RootFacts(AssetsManager manager, AssetsFileInstance file, long rootId)
        {
            var info = file.file.GetAssetInfo(rootId);
            if (info == null || info.TypeId != (int)AssetClassID.GameObject) return null;
            var go = Field(manager, file, rootId);
            var components = go["m_Component"]["Array"];
            if (components.IsDummy) return null;
            ConnectorFacts result = null;
            int physicalCount = 0;
            foreach (var entry in components.Children)
            {
                budget.Check();
                // Unity 5 has pair<int, PPtr<Component>>; newer versions use a component field.
                var pointer = entry["component"];
                if (pointer.IsDummy) pointer = entry["second"];
                if (!Local(pointer)) return null;
                long id = pointer["m_PathID"].AsLong;
                var componentInfo = file.file.GetAssetInfo(id);
                if (componentInfo == null) return null;
                if (componentInfo.TypeId != (int)AssetClassID.MonoBehaviour) continue;
                var component = Field(manager, file, id);
                var scriptPointer = component["m_Script"];
                if (!Local(scriptPointer)) return null;
                var script = Field(manager, file, scriptPointer["m_PathID"].AsLong);
                // Custom root scripts may change native fields at load time: retain the live fallback.
                if (script["m_AssemblyName"].IsDummy || script["m_AssemblyName"].AsString != "Assembly-CSharp.dll") return null;
                string type = script["m_Namespace"].AsString + "." + script["m_ClassName"].AsString;
                ConnectorKind kind;
                if (!knownTypes.TryGetValue(type, out kind)) continue;
                if (++physicalCount > 1) return null;
                string name = kind == ConnectorKind.Attachment ? "Type" : kind == ConnectorKind.Magazine ? "MagazineType" : "ClipType";
                var connector = component[name];
                if (connector.IsDummy || connector.TemplateField.ValueType != AssetValueType.Int32) return null;
                var integrated = component["IsIntegrated"];
                if (kind == ConnectorKind.Magazine && (integrated.IsDummy || integrated.TemplateField.ValueType != AssetValueType.Bool)) return null;
                result = new ConnectorFacts { Kind = kind, Connector = connector.AsInt,
                    Integrated = kind == ConnectorKind.Magazine && integrated.AsBool };
            }
            return result;
        }

        private static bool Local(AssetTypeValueField pointer) => !pointer.IsDummy && !pointer["m_FileID"].IsDummy
            && !pointer["m_PathID"].IsDummy && pointer["m_FileID"].AsInt == 0 && pointer["m_PathID"].AsLong != 0;
        private static AssetTypeValueField Field(AssetsManager manager, AssetsFileInstance file, long id, int max = 256 * 1024)
        {
            var info = file.file.GetAssetInfo(id);
            if (info == null || info.ByteSize > max) throw new NotSupportedException("Missing or oversized metadata record");
            return manager.GetBaseField(file, info);
        }
    }
}
