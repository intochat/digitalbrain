using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Runtime.Hosting;
using Orleans.Storage;
using Orleans.TestingHost;

namespace DigitalBrain.OrleansTests.Features;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FeatureGrainClusterCollection : ICollectionFixture<FeatureGrainClusterFixture>
{
    public const string Name = "feature-grain-cluster";
}

public sealed class FeatureGrainClusterFixture : IAsyncLifetime
{
    public MutableTimeProvider Time { get; } = new(new DateTimeOffset(2026, 7, 13, 8, 0, 0, TimeSpan.Zero));
    public SharedGrainStorage Storage { get; } = new();
    public InProcessTestCluster Cluster { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        builder.ConfigureSilo((_, siloBuilder) => siloBuilder
            .ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(Time);
                services.AddGrainStorage("Default", (_, _) => Storage);
            }));
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public Task DisposeAsync() => Cluster.DisposeAsync().AsTask();

    public TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey =>
        Cluster.Client.GetGrain<TGrain>(key);
}

public sealed class SharedGrainStorage : IGrainStorage
{
    private readonly ConcurrentDictionary<string, Entry> _states = new(StringComparer.Ordinal);
    private long _version;
    private int _nextWriteFailure;
    private Func<object, object>? _nextCommittedState;

    public void FailNextWrite() => Interlocked.Exchange(ref _nextWriteFailure, 1);

    public void CommitThenFailNextWrite() => Interlocked.Exchange(ref _nextWriteFailure, 2);

    public void CommitCompetingStateThenFailNextWrite(Func<object, object> competingState)
    {
        ArgumentNullException.ThrowIfNull(competingState);
        _nextCommittedState = competingState;
        Interlocked.Exchange(ref _nextWriteFailure, 2);
    }

    public Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        if (_states.TryGetValue(Key(stateName, grainId), out var entry))
        {
            grainState.State = (T)entry.State;
            grainState.ETag = entry.ETag;
            grainState.RecordExists = true;
        }
        else
        {
            grainState.State = default!;
            grainState.ETag = null;
            grainState.RecordExists = false;
        }
        return Task.CompletedTask;
    }

    public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        var failure = Interlocked.Exchange(ref _nextWriteFailure, 0);
        if (failure == 1)
            throw new InvalidOperationException("Injected feature storage write failure.");
        var etag = Interlocked.Increment(ref _version).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var committedState = Interlocked.Exchange(ref _nextCommittedState, null)?.Invoke(grainState.State!) ?? grainState.State!;
        _states[Key(stateName, grainId)] = new Entry(committedState, etag);
        grainState.ETag = etag;
        grainState.RecordExists = true;
        if (failure == 2)
            throw new InvalidOperationException("Injected feature storage acknowledgement failure.");
        return Task.CompletedTask;
    }

    public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        _states.TryRemove(Key(stateName, grainId), out _);
        grainState.ETag = null;
        grainState.RecordExists = false;
        return Task.CompletedTask;
    }

    private static string Key(string stateName, GrainId grainId) => $"{stateName}|{grainId}";

    private sealed record Entry(object State, string ETag);
}

public sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
}
