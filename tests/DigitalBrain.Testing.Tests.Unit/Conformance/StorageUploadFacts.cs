using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Testing.Conformance.Storage;

namespace DigitalBrain.Testing.Tests.Unit.Conformance;

public sealed class StorageUploadFacts
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CallerOffsetAndLengthMustMatchBeforeCommit()
    {
        var storage = new InMemoryStorage();
        var caller = "person:alice";
        await using var uploads = new StorageUploadSessions(() => storage, () => caller);
        var id = await uploads.BeginUpload(WritePlan.WriteFile(storage.Root, "result.txt"), Ct);
        caller = "app:alice:assistant";
        await Assert.ThrowsAsync<Denied>(() => uploads.AppendUpload(id, 0, [1], Ct));
        await Assert.ThrowsAsync<Denied>(() => uploads.CommitUpload(id, 0, Ct));
        await Assert.ThrowsAsync<Denied>(() => uploads.AbortUpload(id, Ct));
        caller = "person:alice";
        await uploads.AppendUpload(id, 0, [1, 2, 3], Ct);
        await Assert.ThrowsAsync<Conflict>(() => uploads.AppendUpload(id, 0, [4], Ct));
        await Assert.ThrowsAsync<Conflict>(() => uploads.CommitUpload(id, 4, Ct));
        Assert.Empty((await storage.List(storage.Root, new(), Ct)).Items);
        var item = await uploads.CommitUpload(id, 3, Ct);
        Assert.Equal(new byte[] { 1, 2, 3 }, (await storage.Read(item, null, Ct)).Bytes.ToArray());
        await Assert.ThrowsAsync<NotFound>(() => uploads.CommitUpload(id, 3, Ct));
    }

    [Fact]
    public async Task ExpiryAndDisposalDiscardPendingBytes()
    {
        var storage = new InMemoryStorage();
        var clock = new Clock();
        await using var uploads = new StorageUploadSessions(() => storage, () => "person", clock);
        var id = await uploads.BeginUpload(WritePlan.WriteFile(storage.Root, "expired.txt"), Ct);
        await uploads.AppendUpload(id, 0, [1, 2, 3], Ct);
        clock.Advance(TimeSpan.FromMinutes(11));
        await Assert.ThrowsAsync<NotFound>(() => uploads.CommitUpload(id, 3, Ct));
        Assert.Empty((await storage.List(storage.Root, new(), Ct)).Items);
    }

    [Fact]
    public async Task OversizedChunksAndTooManyOpenUploadsAreRefused()
    {
        var storage = new InMemoryStorage();
        await using var uploads = new StorageUploadSessions(() => storage, () => "person");
        var ids = new List<string>();
        for (var n = 0; n < 8; n++) { ids.Add(await uploads.BeginUpload(WritePlan.WriteFile(storage.Root, $"item-{n}.txt"), Ct)); }
        await Assert.ThrowsAsync<Unavailable>(() => uploads.BeginUpload(WritePlan.WriteFile(storage.Root, "extra.txt"), Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => uploads.AppendUpload(ids[0], 0, new byte[StorageUploadSessions.ChunkSize + 1], Ct));
        foreach (var id in ids) { await uploads.AbortUpload(id, Ct); }
        Assert.Empty((await storage.List(storage.Root, new(), Ct)).Items);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
