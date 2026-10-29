using System.Collections.Concurrent;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Testing.Conformance.Storage;

namespace DigitalBrain.Testing;

/// <summary>
/// A storage@1 fake for <see cref="TestOS"/> and neuron tests: the <see cref="InMemoryStorage"/> reference implementation
/// (it passes the storage conformance kit) plus a record of the <see cref="IStorage"/> members called.
/// Bind it with <see cref="TestOSStorageExtensions.WithStorage(TestOS, string, IStorage)"/>. With <see cref="Seekable"/> false,
/// <see cref="IStorage.OpenRead"/> serves forward-only streams (<see cref="StorageReadStream.CanSeek"/> false, the content stream
/// cannot seek) as a storage without native ranges does; <see cref="MaxSingleRead"/> records the largest single read a consumer
/// asked of any read stream, so a test can assert that content is streamed in chunks rather than buffered whole.
/// </summary>
/// <param name="label">The display label, also the root folder's name.</param>
/// <param name="quota">The quota <see cref="IStorage.Describe"/> reports; unknown when null.</param>
/// <param name="flags">The actions offered; every action by default.</param>
/// <param name="time">The clock for modification times (for example <see cref="TestOS.Clock"/>).</param>
public sealed class FakeStorage(string label = "Fake storage", Quota? quota = null, ItemFlags flags = InMemoryStorage.AllFlags, TimeProvider? time = null)
    : InMemoryStorage(label, quota, flags, null, time, "fake"), IStorage
{
    private readonly ConcurrentQueue<string> _calls = new();
    private int _maxSingleRead;

    /// <summary>The <see cref="IStorage"/> members called, in order.</summary>
    public IReadOnlyList<string> Calls => [.. _calls];

    /// <summary>False serves forward-only read streams (a storage without native ranges). Default true.</summary>
    public bool Seekable { get; set; } = true;

    /// <summary>The largest byte count a single read asked of any stream <see cref="IStorage.OpenRead"/> returned; 0 before any read.</summary>
    public int MaxSingleRead => Volatile.Read(ref _maxSingleRead);

    /// <summary>Opens a read stream as <see cref="InMemoryStorage.OpenRead"/> does; forward-only when <see cref="Seekable"/> is false, and every read is measured for <see cref="MaxSingleRead"/>.</summary>
    public new async Task<StorageReadStream> OpenRead(ItemId item, ByteRange? range, CancellationToken ct = default)
    {
        var inner = await base.OpenRead(item, range, ct).ConfigureAwait(false);
        var seekable = Seekable;
        return new StorageReadStream(new MeasuredStream(inner.Content, seekable, Measure), inner.Length, inner.ContentType, inner.ETag, seekable && inner.CanSeek, inner.Modified);
    }

    /// <inheritdoc />
    protected override void OnCall(string member) => _calls.Enqueue(member);

    private void Measure(int count)
    {
        var seen = Volatile.Read(ref _maxSingleRead);
        while (count > seen)
        {
            var was = Interlocked.CompareExchange(ref _maxSingleRead, count, seen);
            if (was == seen) { return; }
            seen = was;
        }
    }

    // Wraps a read stream: measures each read and, when not seekable, refuses seeking as a forward-only network stream does.
    private sealed class MeasuredStream(Stream inner, bool seekable, Action<int> measure) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => seekable;

        public override bool CanWrite => false;

        public override long Length => seekable ? inner.Length : throw new NotSupportedException("The stream is forward-only.");

        public override long Position
        {
            get => seekable ? inner.Position : throw new NotSupportedException("The stream is forward-only.");
            set => _ = seekable ? inner.Position = value : throw new NotSupportedException("The stream is forward-only.");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            measure(count);
            return inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            measure(buffer.Length);
            return inner.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            measure(buffer.Length);
            return inner.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            measure(count);
            return inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
            => seekable ? inner.Seek(offset, origin) : throw new NotSupportedException("The stream is forward-only.");

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) { inner.Dispose(); }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>The storage@1 helpers over <see cref="TestOS.With{TCapability}(string, TCapability)"/>.</summary>
public static class TestOSStorageExtensions
{
    /// <summary>Binds <paramref name="storage"/> as the storage@1 connection named <paramref name="name"/>.</summary>
    public static TestOS WithStorage(this TestOS os, string name, IStorage storage)
    {
        ArgumentNullException.ThrowIfNull(os);
        return os.With(name, storage);
    }

    /// <summary>Binds a new <see cref="FakeStorage"/> (on the TestOS clock) as the storage@1 connection named <paramref name="name"/>.</summary>
    /// <param name="os">The TestOS.</param>
    /// <param name="name">The connection name.</param>
    /// <param name="label">The label; <paramref name="name"/> when null.</param>
    /// <param name="quota">The quota; unknown when null.</param>
    /// <param name="configure">Seeds or adjusts the fake before binding.</param>
    public static TestOS WithStorage(this TestOS os, string name, string? label = null, Quota? quota = null, Action<FakeStorage>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(os);
        var fake = new FakeStorage(label ?? name, quota, time: os.Clock);
        configure?.Invoke(fake);
        return os.With<IStorage>(name, fake);
    }
}
