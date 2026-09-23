using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Unit;
using Orleans;
using Orleans.Runtime;
using Xunit;
namespace DigitalBrain.Tests;

public interface ICounter : INeuron
{
    Task<int> Read();
    Task Set(int value);
    Task SetWithoutPublishing(int value);
}
[GenerateSerializer]
public sealed class CounterState { [Id(0)] public int Value { get; set; } }
[GrainType("test-counter")]
public sealed class CounterNeuron([PersistentState("state", "Default")] IPersistentState<CounterState> state) : Neuron, ICounter
{
    public Task<int> Read() => Task.FromResult(state.State.Value);
    public async Task Set(int value)
    {
        await SetWithoutPublishing(value);
        await PublishAsync(new Number(value));
    }
    public async Task SetWithoutPublishing(int value)
    {
        state.State = new CounterState { Value = value };
        await state.WriteStateAsync();
    }
}

[GenerateSerializer]
public sealed class ProbeState { [Id(0)] public int Value { get; set; } }

public sealed class ProbeNeuron(IPersistentState<ProbeState> state) : Neuron<ProbeState>(state)
{
    public int Value => Snapshot.Value;
    public Task Set(int value) => Save(new ProbeState { Value = value }, new Number(value));
}

internal sealed class FailingProbeState(ProbeState initial) : IPersistentState<ProbeState>
{
    public ProbeState State { get; set; } = initial;
    public string Etag => string.Empty;
    public bool RecordExists => true;
    public bool FailNextWrite { get; set; }
    public Task ClearStateAsync() => Task.CompletedTask;
    public Task ReadStateAsync() => Task.CompletedTask;
    public Task WriteStateAsync() => FailNextWrite
        ? Task.FromException(new InvalidOperationException("Storage is unavailable."))
        : Task.CompletedTask;
}

public sealed class PersistenceFacts
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException($"Repository root (DigitalBrain.slnx) was not found above {AppContext.BaseDirectory}.");
    }

    private static IEnumerable<string> BuildFiles()
    {
        var root = new DirectoryInfo(RepositoryRoot);
        foreach (var file in root.EnumerateFiles("*.props", SearchOption.TopDirectoryOnly))
        {
            yield return file.FullName;
        }
        foreach (var file in root.EnumerateFiles("*.csproj", SearchOption.AllDirectories))
        {
            if (file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            yield return file.FullName;
        }
    }

    [Fact]
    public async Task FailedWriteRollsStateBack()
    {
        var storage = new FailingProbeState(new ProbeState { Value = 7 });
        var neuron = new ProbeNeuron(storage);
        storage.FailNextWrite = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => neuron.Set(9));
        Assert.Equal(7, neuron.Value);
        Assert.Equal(7, storage.State.Value);
    }

    [Fact]
    public async Task ClusterDocumentStoreWritesThroughGrainState()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        var store = new GrainDocumentStore<CounterState>(brain.Grains(), "persistence-facts");
        await store.UpdateAsync("doc", state => state.Value = 42, ct);
        var (_, payload) = await brain.Grains().GetGrain<IDocumentGrain>("persistence-facts/doc").ReadAsync();
        Assert.Contains("42", payload ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(42, await store.ReadAsync("doc", state => state.Value, ct));
    }

    [Fact]
    public void JournalingPackageIsNotReferenced()
    {
        Assert.False(File.Exists(Path.Combine(RepositoryRoot,
                "src", "Modules", "DigitalBrain", "Kernel", "Aspire", "AzureOrleansJournalHosting.cs")),
            "AzureOrleansJournalHosting is the unwired Orleans Journaling host and must be deleted.");

        var offenders = BuildFiles()
            .Where(file => File.ReadAllText(file).Contains("Microsoft.Orleans.Journaling", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepositoryRoot, file))
            .ToArray();
        Assert.True(offenders.Length == 0, $"Orleans Journaling must not be referenced by any package file: {string.Join(", ", offenders)}");
    }

    [Fact]
    public async Task DeactivationKeepsStateAndDoesNotReplaySignals()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        var counter = brain.Get<ICounter>("saved");
        await counter.SetWithoutPublishing(23);
        await brain.DeactivateAsync(counter, ct);
        Assert.Equal(23, await counter.Read());
        await using var probe = await brain.Observe<Number>(counter, ct);
        await counter.Set(24);
        Assert.Equal(24, (await probe.NextAsync(ct: ct)).Value);
        Assert.DoesNotContain(probe.Snapshot, fact => fact.Value == 23);
    }
}