using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Serialization;
using Xunit;
namespace DigitalBrain.Tests;

public sealed class StorageAdapterFacts
{
    [Fact]
    public async Task StoreCopiesStateChecksEtagsAndFailsOnCorruption()
    {
        var directory = Path.Combine(Path.GetTempPath(), "brain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var services = new ServiceCollection().AddSerializer().BuildServiceProvider();
            var store = new FileGrainStorage(directory, services.GetRequiredService<Serializer>());
            var id = GrainId.Create("counter", "one");
            var state = new GrainState<CounterState>(new() { Value = 3 });
            await store.WriteStateAsync("state", id, state);
            var savedTag = state.ETag;
            state.State!.Value = 99;
            var read = new GrainState<CounterState>(new());
            await store.ReadStateAsync("state", id, read);
            Assert.Equal(3, read.State!.Value);
            read.State!.Value = 4;
            await store.WriteStateAsync("state", id, read);
            await Assert.ThrowsAsync<Orleans.Storage.InconsistentStateException>(() => store.WriteStateAsync("state", id, state));
            Assert.Equal(savedTag, state.ETag);
            await store.ClearStateAsync("state", id, read);
            Assert.False(read.RecordExists);
            Assert.Equal(0, read.State!.Value);
            state.State!.Value = 90;
            await store.ReadStateAsync("state", id, state);
            Assert.False(state.RecordExists);
            Assert.Null(state.ETag);
            Assert.Equal(0, state.State!.Value);
            await store.WriteStateAsync("state", id, state);
            await File.WriteAllBytesAsync(Directory.GetFiles(directory, "*.state").Single(), [1], TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadStateAsync("state", id, state));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task HostRejectsConcurrentOwnershipAndReleasesHeldReadsOnDisposal()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), "brain-" + Guid.NewGuid().ToString("N"));
        try
        {
            var faults = new StorageFaults();
            await using var host = await BrainTestHost.StartAsync(new() { PersistenceDirectory = directory, StorageFaults = faults }, ct);
            await Assert.ThrowsAsync<IOException>(() => BrainTestHost.StartAsync(new() { PersistenceDirectory = directory }, ct));
            var counter = host.Brain.Get<ICounter>("held");
            using var hold = faults.HoldNextRead(counter.GetGrainId());
            var read = counter.Read();
            await hold.Entered.WaitAsync(TimeSpan.FromSeconds(5), ct);
            await host.DisposeAsync();
            await hold.WaitAsync().WaitAsync(TimeSpan.FromSeconds(1), ct);
            // The client may stop before the released read's RPC response arrives.
            _ = read.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        finally { Directory.Delete(directory, true); }
    }
}

