using System;
using System.IO;
using System.Threading;
using AssetsTools.NET;
using Gundomizer.Indexing;
using LZ4ps;

internal static class BlockStreamChecks
{
    internal static void Run()
    {
        var random = new Random(7341);
        var raw = new MemoryStream(); var packed = new MemoryStream();
        var blocks = new AssetBundleBlockInfo[9];
        for (int i = 0; i < blocks.Length; ++i)
        {
            var bytes = new byte[500 + i * 341];
            random.NextBytes(bytes);
            if (i % 2 == 0) for (int j = 12; j < bytes.Length; ++j) bytes[j] = bytes[j % 12];
            raw.Write(bytes, 0, bytes.Length);
            var compressed = i % 3 == 0 ? bytes : LZ4Codec.Encode32(bytes, 0, bytes.Length);
            packed.Write(compressed, 0, compressed.Length);
            blocks[i] = new AssetBundleBlockInfo { CompressedSize = (uint)compressed.Length,
                DecompressedSize = (uint)bytes.Length, Flags = (ushort)(i % 3 == 0 ? 0 : 3) };
        }
        byte[] expected = raw.ToArray(), actual = new byte[expected.Length];
        var pool = new BlockBuffers(); var stop = new ManualResetEvent(false);
        var stream = new ReusableBlockStream(packed, 0, blocks, pool, new ReadBudget(stop, false));
        Require(stream.Read(actual, 0, actual.Length) == expected.Length, "read spanning mixed compressed/raw blocks");
        Equal(expected, actual, 0, 0, expected.Length);
        Require(stream.Read(actual, 0, 1) == 0 && stream.Read(actual, 0, 0) == 0, "EOF and empty reads");
        for (int i = 0; i < 400; ++i)
        {
            int position = random.Next(expected.Length), count = random.Next(1, 5000);
            stream.Position = position;
            int take = stream.Read(actual, 7, count);
            Require(take == Math.Min(count, expected.Length - position), "bounded random read");
            Equal(expected, actual, position, 7, take);
        }
        Require(stream.Seek(-12, SeekOrigin.End) == expected.Length - 12, "end-relative seek");
        stream.Position = 0; stream.Read(actual, 0, 1); // Ensure slot capacities have warmed up.
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; ++i) { stream.Position = 0; stream.Read(actual, 0, actual.Length); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated < 4096, "repeated block eviction and decompression reuse buffers (bytes=" + allocated + ")");
        stop.Set();
        bool cancelled = false;
        try { stream.Position = 0; stream.Read(actual, 0, 1); } catch (OperationCanceledException) { cancelled = true; }
        Require(cancelled, "cached-block reads still obey cancellation");
        var invalid = new[] { new AssetBundleBlockInfo { CompressedSize = 1, DecompressedSize = 2, Flags = 0 } };
        bool rejected = false;
        try { new ReusableBlockStream(packed, 0, invalid, pool, new ReadBudget(new ManualResetEvent(false), false)); }
        catch (NotSupportedException) { rejected = true; }
        Require(rejected, "inconsistent raw block rejected");
        byte[] truncated = { 0xF0, 0xFF };
        invalid[0] = new AssetBundleBlockInfo { CompressedSize = 2, DecompressedSize = 32, Flags = 3 };
        rejected = false;
        try
        {
            var broken = new ReusableBlockStream(new MemoryStream(truncated), 0, invalid, pool, new ReadBudget(new ManualResetEvent(false), false));
            broken.Read(actual, 0, 32);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is IndexOutOfRangeException) { rejected = true; }
        Require(rejected, "truncated compressed data rejected instead of reusing stale output");
        Console.WriteLine("PASS reusable block stream: mixed sizes, cross-block/random reads, buffer reuse, cancellation and malformed data");
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception("FAIL " + message); }
    private static void Equal(byte[] expected, byte[] actual, int from, int to, int size)
    { for (int i = 0; i < size; ++i) Require(expected[from + i] == actual[to + i], "decoded bytes agree"); }
}
