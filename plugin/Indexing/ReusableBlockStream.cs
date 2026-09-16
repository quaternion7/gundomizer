using System;
using System.IO;
using AssetsTools.NET;
using LZ4ps;

namespace Gundomizer.Indexing
{
    // One worker owns these buffers across bundles. Evicting a block reuses its array
    // instead of making Unity's shared collector reclaim compressed/decompressed copies.
    internal sealed class BlockBuffers
    {
        internal byte[] Compressed = new byte[0];
        internal readonly byte[][] Decoded = new byte[4][];
        internal byte[] Input(int size)
        {
            if (Compressed.Length < size) Compressed = new byte[size];
            return Compressed;
        }
        internal byte[] Output(int slot, int size)
        {
            if (Decoded[slot] == null || Decoded[slot].Length < size) Decoded[slot] = new byte[size];
            return Decoded[slot];
        }
    }

    internal sealed class ReusableBlockStream : Stream
    {
        private readonly Stream source;
        private readonly AssetBundleBlockInfo[] blocks;
        private readonly long[] compressedOffsets, decodedOffsets;
        private readonly BlockBuffers buffers;
        private readonly ReadBudget budget;
        private readonly int[] cached = { -1, -1, -1, -1 };
        private int nextSlot;
        private long position;
        internal ReusableBlockStream(Stream source, long offset, AssetBundleBlockInfo[] blocks, BlockBuffers buffers, ReadBudget budget)
        {
            this.source = source; this.blocks = blocks; this.buffers = buffers; this.budget = budget;
            compressedOffsets = new long[blocks.Length + 1]; decodedOffsets = new long[blocks.Length + 1];
            compressedOffsets[0] = offset;
            for (int i = 0; i < blocks.Length; ++i)
            {
                budget.Check();
                var block = blocks[i]; int compression = block.GetCompressionType();
                if (block.DecompressedSize == 0 || block.DecompressedSize > 1024 * 1024
                    || block.CompressedSize == 0 || block.CompressedSize > 2 * 1024 * 1024
                    || (compression != 0 && compression != 2 && compression != 3)
                    || (compression == 0 && block.CompressedSize != block.DecompressedSize))
                    throw new NotSupportedException("Unsupported or oversized bundle block");
                compressedOffsets[i + 1] = checked(compressedOffsets[i] + block.CompressedSize);
                decodedOffsets[i + 1] = checked(decodedOffsets[i] + block.DecompressedSize);
            }
            if (offset < 0 || compressedOffsets[blocks.Length] > source.Length) throw new EndOfStreamException();
        }
        public override long Length => decodedOffsets[blocks.Length];
        public override long Position
        {
            get => position;
            set { if (value < 0 || value > Length) throw new ArgumentOutOfRangeException("value"); position = value; }
        }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException();
            int total = 0;
            while (total < count && position < Length)
            {
                budget.Check();
                int index = Array.BinarySearch(decodedOffsets, position);
                if (index < 0) index = ~index - 1;
                int slot = Array.IndexOf(cached, index);
                if (slot < 0)
                {
                    slot = nextSlot; nextSlot = (nextSlot + 1) % cached.Length;
                    cached[slot] = -1;
                    var block = blocks[index];
                    int packedSize = (int)block.CompressedSize, size = (int)block.DecompressedSize;
                    byte[] output = buffers.Output(slot, size);
                    source.Position = compressedOffsets[index];
                    if (block.GetCompressionType() == 0) ReadExactly(output, size);
                    else
                    {
                        byte[] input = buffers.Input(packedSize);
                        ReadExactly(input, packedSize);
                        if (LZ4Codec.Decode32(input, 0, packedSize, output, 0, size, false) != size)
                            throw new IOException("Invalid decoded bundle block size");
                    }
                    cached[slot] = index;
                }
                int start = (int)(position - decodedOffsets[index]);
                int take = Math.Min(count - total, (int)blocks[index].DecompressedSize - start);
                Buffer.BlockCopy(buffers.Decoded[slot], start, buffer, offset + total, take);
                total += take; position += take;
            }
            return total;
        }
        private void ReadExactly(byte[] buffer, int size)
        {
            int done = 0;
            while (done < size)
            {
                budget.Check();
                int read = source.Read(buffer, done, size - done);
                if (read == 0) throw new EndOfStreamException();
                done += read;
            }
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            long start = origin == SeekOrigin.Begin ? 0 : origin == SeekOrigin.Current ? position
                : origin == SeekOrigin.End ? Length : throw new ArgumentException("origin");
            Position = checked(start + offset); return position;
        }
        public override void Flush() { }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        // The bundle owns the source stream; closing a per-file reader must not close it.
    }
}
