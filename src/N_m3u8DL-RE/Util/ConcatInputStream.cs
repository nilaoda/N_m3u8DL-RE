using N_m3u8DL_RE.Common.Resource;

namespace N_m3u8DL_RE.Util;

// 在多个分片上提供可 seek 的只读流；只保留当前分片的句柄，支持 MP4 box 读取和虚拟输入传输。
internal sealed class ConcatInputStream : Stream
{
    private readonly string[] files;
    private readonly long[] ends;
    private FileStream? source;
    private int sourceIndex = -1;
    private long position;
    private bool disposed;

    internal ConcatInputStream(string[] files)
    {
        this.files = files.Select(Path.GetFullPath).ToArray();
        ends = new long[files.Length];
        long length = 0;
        for (var i = 0; i < files.Length; i++)
        {
            length = checked(length + new FileInfo(this.files[i]).Length);
            ends[i] = length;
        }
    }

    internal ConcatInputStream(string[] files, long[] ends)
    {
        this.files = files;
        this.ends = ends;
    }

    public override bool CanRead => !disposed;
    public override bool CanSeek => !disposed;
    public override bool CanWrite => false;
    public override long Length => ends.Length == 0 ? 0 : ends[^1];
    public override long Position
    {
        get => position;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            position = value;
        }
    }

    private FileStream OpenSource()
    {
        // 找到第一个结束位置大于当前位置的分片，同时跳过空分片。
        var low = 0;
        var high = ends.Length;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (ends[mid] <= position)
                low = mid + 1;
            else
                high = mid;
        }
        if (sourceIndex != low)
        {
            source?.Dispose();
            source = null;
            sourceIndex = -1;
            // 调用方复用读取缓冲；关闭 FileStream 内部缓冲，避免分片尾部
            // 的小块读取再分配一份缓冲并复制数据。
            var stream = new FileStream(files[low], FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var start = low == 0 ? 0 : ends[low - 1];
            if (stream.Length != ends[low] - start)
            {
                stream.Dispose();
                throw new IOException(ResString.concatInputLengthChanged);
            }
            source = stream;
            sourceIndex = low;
        }
        source!.Position = position - (low == 0 ? 0 : ends[low - 1]);
        return source;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var total = 0;
        while (total < buffer.Length && position < Length)
        {
            var stream = OpenSource();
            var count = (int)Math.Min(buffer.Length - total, ends[sourceIndex] - position);
            var read = stream.Read(buffer.Slice(total, count));
            if (read == 0)
                throw new EndOfStreamException();
            position += read;
            total += read;
        }
        return total;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var total = 0;
        while (total < buffer.Length && position < Length)
        {
            token.ThrowIfCancellationRequested();
            var stream = OpenSource();
            var count = (int)Math.Min(buffer.Length - total, ends[sourceIndex] - position);
            var read = await stream.ReadAsync(buffer.Slice(total, count), token).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException();
            position += read;
            total += read;
        }
        return total;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(position + offset),
            SeekOrigin.End => checked(Length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        return position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            source?.Dispose();
        disposed = true;
        base.Dispose(disposing);
    }
}
