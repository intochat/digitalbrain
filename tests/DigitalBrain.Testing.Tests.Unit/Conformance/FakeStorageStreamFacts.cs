using DigitalBrain.Capabilities.Storage;

namespace DigitalBrain.Testing.Tests.Unit.Conformance;

// 07-02: FakeStorage non-seekable mode and the MaxSingleRead helper the content gateway facts (07-07) use.
public sealed class FakeStorageStreamFacts
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Fake_storage_non_seekable_mode_serves_forward_only_streams_and_measures_single_reads()
    {
        var storage = new FakeStorage("Fake") { Seekable = false };
        var file = storage.AddFile(storage.Root, "big.bin", new byte[100_000]);

        await using var read = await ((IStorage)storage).OpenRead(file, null, Ct);
        Assert.False(read.CanSeek);
        Assert.False(read.Content.CanSeek);
        Assert.Throws<NotSupportedException>(() => read.Content.Seek(0, SeekOrigin.Begin));
        var buffer = new byte[4096];
        var total = 0;
        int n;
        while ((n = await read.Content.ReadAsync(buffer, Ct)) > 0) { total += n; }

        Assert.Equal(100_000, total);
        Assert.Equal(4096, storage.MaxSingleRead);
        Assert.Contains("OpenRead", storage.Calls);
    }
}
