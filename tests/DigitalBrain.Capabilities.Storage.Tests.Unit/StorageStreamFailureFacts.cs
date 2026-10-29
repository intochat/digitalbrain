namespace DigitalBrain.Capabilities.Storage.Tests.Unit;

public sealed class StorageStreamFailureFacts
{
    [Fact]
    public async Task CommitFailureKeepsItsCauseWhenAbortAlsoFails()
    {
        var write = new FailingWrite { AbortFails = true };
        var error = await Assert.ThrowsAsync<IOException>(() => write.Commit(TestContext.Current.CancellationToken));
        Assert.Equal("Commit failed.", error.Message);
        Assert.IsType<IOException>(error.Data["StorageAbortFailure"]);
        await write.DisposeAsync();
    }

    [Fact]
    public async Task DisposalClosesTheStreamEvenWhenAbortFails()
    {
        var write = new FailingWrite { AbortFails = true };
        await Assert.ThrowsAsync<IOException>(async () => await write.DisposeAsync());
        Assert.False(write.Content.CanWrite);
    }

    [Fact]
    public async Task AFailedCommitAbortsItsStagingBeforeDisposal()
    {
        var write = new FailingWrite();
        await Assert.ThrowsAsync<IOException>(() => write.Commit(TestContext.Current.CancellationToken));
        Assert.Equal(1, write.Aborted);
        await write.DisposeAsync();
        Assert.Equal(1, write.Aborted);
    }

    private sealed class FailingWrite() : StorageWriteStream(new MemoryStream())
    {
        public int Aborted { get; private set; }
        public bool AbortFails { get; init; }
        protected override Task<ItemId> CommitCore(CancellationToken ct) => throw new IOException("Commit failed.");
        protected override Task AbortCore(CancellationToken ct)
        {
            Aborted++;
            if (AbortFails) { throw new IOException("Abort failed."); }
            return Task.CompletedTask;
        }
    }
}
