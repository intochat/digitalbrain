using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Kernel.Features;
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
    public FeatureSuggestionTestChatClient SuggestionModel { get; } = new();
    public TestFeaturePublicationVerifier PublicationVerifier { get; } = new();
    public InProcessTestCluster Cluster { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        builder.ConfigureSilo((_, siloBuilder) => siloBuilder
            .ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(Time);
                services.AddSingleton<IChatClient>(SuggestionModel);
                services.AddSingleton<IFeaturePublicationVerifier>(PublicationVerifier);
                services.AddGrainStorage("Default", (_, _) => Storage);
            }));
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public Task DisposeAsync() => Cluster.DisposeAsync().AsTask();

    public TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey =>
        Cluster.Client.GetGrain<TGrain>(key);

    public async Task<FeaturePublicationReceipt> PublishActiveAsync(
        BrainOwnerId ownerId,
        IFeatureHubGrain hub,
        FeatureInstallationId installationId)
    {
        var ticket = await hub.PrepareActivePublicationAsync(installationId);
        var receipt = new FeaturePublicationReceipt(
            installationId,
            ticket.PublicationFence,
            ticket.AuthorityDigest,
            ticket.AccessDigest,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                FeaturePublicationManifestCodec.Serialize(ownerId, ticket))));
        PublicationVerifier.Allow(ownerId, ticket, receipt);
        return await hub.ConfirmActivePublicationAsync(receipt);
    }
}

public sealed class TestFeaturePublicationVerifier : IFeaturePublicationVerifier
{
    private readonly ConcurrentDictionary<PublicationKey, byte> allowed = new();
    private readonly ConcurrentDictionary<string, Exception> failures = new(StringComparer.Ordinal);

    public void Fail(BrainOwnerId ownerId, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        failures[ownerId.Value] = exception;
    }

    public void Allow(
        BrainOwnerId ownerId,
        FeaturePublicationTicket ticket,
        FeaturePublicationReceipt receipt)
    {
        var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            FeaturePublicationManifestCodec.Serialize(ownerId, ticket)));
        if (ticket.InstallationId != receipt.InstallationId ||
            ticket.PublicationFence != receipt.PublicationFence ||
            !string.Equals(ticket.AuthorityDigest, receipt.AuthorityDigest, StringComparison.Ordinal) ||
            !string.Equals(ticket.AccessDigest, receipt.AccessDigest, StringComparison.Ordinal) ||
            !string.Equals(digest, receipt.ManifestDigest, StringComparison.Ordinal))
            throw new ArgumentException("Only an exact test publication can be allowed.", nameof(receipt));
        allowed.TryAdd(Key(ownerId, receipt), 0);
    }

    public Task VerifyAsync(
        BrainOwnerId ownerId,
        FeaturePublicationTicket ticket,
        FeaturePublicationReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (failures.TryRemove(ownerId.Value, out var failure))
            return Task.FromException(failure);
        if (!allowed.ContainsKey(Key(ownerId, receipt)))
            throw new FeatureConcurrencyException(
                "The exact active Feature publication was not produced by the test publisher.",
                FeatureCommandRejectionReason.Precondition);
        var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            FeaturePublicationManifestCodec.Serialize(ownerId, ticket)));
        if (!string.Equals(digest, receipt.ManifestDigest, StringComparison.Ordinal))
            throw new FeatureConcurrencyException(
                "The test publication does not match the current ticket.",
                FeatureCommandRejectionReason.Precondition);
        return Task.CompletedTask;
    }

    private static PublicationKey Key(BrainOwnerId ownerId, FeaturePublicationReceipt receipt) => new(
        ownerId.Value,
        receipt.InstallationId.Value,
        receipt.PublicationFence,
        receipt.AuthorityDigest,
        receipt.AccessDigest,
        receipt.ManifestDigest);

    private sealed record PublicationKey(
        string OwnerId,
        string InstallationId,
        long PublicationFence,
        string AuthorityDigest,
        string AccessDigest,
        string ManifestDigest);
}

public sealed class FeatureSuggestionTestChatClient : IChatClient
{
    private string _response = string.Empty;
    private Func<Task>? _beforeResponse;

    public int CallCount { get; private set; }
    public ChatOptions? LastOptions { get; private set; }
    public string? LastPrompt { get; private set; }

    public void RespondWith(string response, Func<Task>? beforeResponse = null)
    {
        _response = response;
        _beforeResponse = beforeResponse;
        CallCount = 0;
        LastOptions = null;
        LastPrompt = null;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastOptions = options;
        LastPrompt = string.Join("\n", messages.Select(message => message.Text));
        var callback = _beforeResponse;
        _beforeResponse = null;
        if (callback is not null) await callback();
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, _response));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
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
