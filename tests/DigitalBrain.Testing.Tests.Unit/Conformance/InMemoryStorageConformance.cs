using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Testing.Conformance.Storage;

namespace DigitalBrain.Testing.Tests.Unit.Conformance;

// The reference subject: InMemoryStorage must pass every storage@1 fact (only the read-only stream fact skips).
public sealed class InMemoryStorageConformance : StorageConformance<InMemoryStorage>
{
    internal static readonly IReadOnlyList<ItemId> Hostile =
    [
        new("../outside"),
        new("..\\..\\Windows\\win.ini"),
        new("/etc/passwd"),
        new("C:\\Windows\\System32"),
        new("m:../escape"),
    ];

    protected override Task<StorageSubject> Connect(StorageSeed seed, CancellationToken ct)
    {
        var storage = new InMemoryStorage("Kit memory");
        storage.Seed(seed);
        return Task.FromResult(new StorageSubject(storage, storage.Operations, Hostile));
    }
}

// The same reference subject with a read-only scope: write facts skip, StreamWriteUnsupportedWithoutWrite runs.
public sealed class InMemoryReadOnlyStorageConformance : StorageConformance<InMemoryStorage>
{
    protected override Task<StorageSubject> Connect(StorageSeed seed, CancellationToken ct)
    {
        var storage = new InMemoryStorage("Kit read-only", flags: ItemFlags.Read | ItemFlags.Search);
        storage.Seed(seed);
        return Task.FromResult(new StorageSubject(storage, storage.Operations, InMemoryStorageConformance.Hostile));
    }
}

// The TestOS fake passes the kit too (07-02 relies on it).
public sealed class FakeStorageConformance : StorageConformance<FakeStorage>
{
    protected override Task<StorageSubject> Connect(StorageSeed seed, CancellationToken ct)
    {
        var storage = new FakeStorage("Kit fake", Quota.Known(10, 100));
        storage.Seed(seed);
        return Task.FromResult(new StorageSubject(storage, storage.Operations, InMemoryStorageConformance.Hostile));
    }
}

// FakeStorage in its non-seekable mode (forward-only read streams) passes the kit too.
public sealed class FakeNonSeekableStorageConformance : StorageConformance<FakeStorage>
{
    protected override Task<StorageSubject> Connect(StorageSeed seed, CancellationToken ct)
    {
        var storage = new FakeStorage("Kit fake", Quota.Known(10, 100)) { Seekable = false };
        storage.Seed(seed);
        return Task.FromResult(new StorageSubject(storage, storage.Operations, InMemoryStorageConformance.Hostile));
    }
}
