using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Gundomizer.Indexing
{
    // Cooperative CPU pacing, cancellation and an independent I/O rate limit. No Unity APIs.
    internal sealed class ReadBudget
    {
        private readonly ManualResetEvent stop;
        private readonly Stopwatch slice = Stopwatch.StartNew();
        private readonly Stopwatch io = Stopwatch.StartNew();
        private long ioBytes;
        internal readonly bool Paced;
        internal ReadBudget(ManualResetEvent stop, bool paced) { this.stop = stop; Paced = paced; }
        internal void Check(int bytes = 0)
        {
            if (stop.WaitOne(0, false)) throw new OperationCanceledException();
            if (!Paced) return;
            ioBytes += bytes;
            if (slice.ElapsedMilliseconds >= 2)
            {
                if (stop.WaitOne(8, false)) throw new OperationCanceledException();
                slice.Reset(); slice.Start();
            }
            // At most approximately 8 MiB/s, in <=128 KiB bursts. Does not throttle Unity's loader.
            if (ioBytes >= 128 * 1024)
            {
                int wait = (int)Math.Min(1000, ioBytes * 1000 / (8 * 1024 * 1024) - io.ElapsedMilliseconds);
                if (wait > 0 && stop.WaitOne(wait, false)) throw new OperationCanceledException();
                ioBytes = 0; io.Reset(); io.Start();
            }
        }
    }

    internal sealed class BudgetStream : Stream
    {
        private readonly Stream inner;
        private readonly ReadBudget budget;
        internal long BytesRead;
        internal BudgetStream(Stream inner, ReadBudget budget) { this.inner = inner; this.budget = budget; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            budget.Check();
            int read = inner.Read(buffer, offset, Math.Min(count, 128 * 1024));
            BytesRead += read;
            budget.Check(read);
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
