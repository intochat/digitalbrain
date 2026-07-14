using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Features.Sdk;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.FeatureHost;

public interface IFeatureCapabilityClient
{
    Task<JsonElement> ExecuteAsync(CapabilityRequest request, CancellationToken cancellationToken = default);
}

public sealed class CapabilityFeatureRunContextFactory(
    IFeatureCapabilityClient capabilities,
    TimeProvider timeProvider) : IFeatureRunContextFactory
{
    public ValueTask<IFeatureRunContext> CreateAsync(
        FeatureWorkItem work,
        FeatureRunClaim claim,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IFeatureRunContext>(new CapabilityFeatureRunContext(
            work,
            claim,
            capabilities,
            timeProvider,
            cancellationToken));
    }
}

internal sealed class CapabilityFeatureRunContext :
    IFeatureRunContext,
    IFeatureContext,
    IFeatureClock,
    IFeatureIdentifiers,
    IFeatureState,
    IMemoryRecall,
    IMemoryRemember,
    IModelWorkflow,
    IFeatureIntentBuffer
{
    private const int MaximumReads = 20;
    private const int MaximumModelCalls = 4;
    private const int MaximumIntents = 32;
    private readonly FeatureWorkItem _work;
    private readonly FeatureRunClaim _claim;
    private readonly IFeatureCapabilityClient _capabilities;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _runLifetime;
    private readonly List<FeatureIntent> _intents = [];
    private readonly object _gate = new();
    private string _stateJson;
    private int _sequence;
    private int _reads;
    private int _modelCalls;
    private int _inFlight;
    private TaskCompletionSource? _drained;
    private bool _sealed;
    private bool _disposed;

    internal CapabilityFeatureRunContext(
        FeatureWorkItem work,
        FeatureRunClaim claim,
        IFeatureCapabilityClient capabilities,
        TimeProvider timeProvider,
        CancellationToken runCancellation)
    {
        _work = work;
        _claim = claim;
        _capabilities = capabilities;
        _timeProvider = timeProvider;
        _runLifetime = CancellationTokenSource.CreateLinkedTokenSource(runCancellation);
        _stateJson = new FeatureState(claim.StateJson).Json;
    }

    public IFeatureContext Context => this;
    public IFeatureClock Clock => this;
    public IFeatureIdentifiers Identifiers => this;
    public IFeatureState State => this;
    public IMemoryRecall MemoryRecall => this;
    public IMemoryRemember MemoryRemember => this;
    public IModelWorkflow Models => this;
    public IFeatureIntentBuffer Intents => this;
    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
            {
                ThrowIfClosed();
                return _timeProvider.GetUtcNow();
            }
        }
    }

    public string Next(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (scope.Length > 128 || scope.Any(char.IsControl))
            throw new ArgumentException("A bounded identifier scope is required.", nameof(scope));
        lock (_gate)
        {
            ThrowIfClosed();
            var value = $"{_claim.Input.InputId}\0{scope}\0{++_sequence}";
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        }
    }

    public FeatureState Read()
    {
        lock (_gate)
        {
            ThrowIfClosed();
            return new FeatureState(_stateJson);
        }
    }

    public void Replace(FeatureState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            ThrowIfClosed();
            _stateJson = state.Json;
        }
    }

    public async Task<IReadOnlyList<MemoryFact>> RecallAsync(
        MemoryRecallRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var payload = await ExecuteAsync(
            "memory.recall",
            null,
            Next("memory-recall"),
            JsonSerializer.SerializeToElement(new { request.Query, request.Tags, request.Limit }),
            modelCall: false,
            cancellationToken);
        return payload.GetProperty("facts").Deserialize<MemoryFact[]>(FeatureJson.Options) ?? [];
    }

    public void Remember(MemoryRememberIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        AddIntent(new FeatureIntent(
            intent.LogicalOperationKey,
            FeatureIntentKind.InternalWrite,
            JsonSerializer.Serialize(new { intent.FactId, intent.Text, intent.Tags }, FeatureJson.Options)));
    }

    public async Task<ModelResponse> CompleteAsync(
        ModelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var payload = await ExecuteAsync(
            "model.complete.v1",
            null,
            request.LogicalOperationKey,
            JsonSerializer.SerializeToElement(new { request.WorkflowId, request.Prompt }, FeatureJson.Options),
            modelCall: true,
            cancellationToken);
        return new ModelResponse(payload.GetProperty("text").GetString() ?? string.Empty);
    }

    public void AddTextSurface(TextSurfaceIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        AddIntent(new FeatureIntent(
            intent.LogicalOperationKey,
            FeatureIntentKind.TextSurface,
            JsonSerializer.Serialize(new { intent.Title, intent.Text }, FeatureJson.Options)));
    }

    public void EmitEvent(EventIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        AddIntent(new FeatureIntent(
            intent.LogicalOperationKey,
            FeatureIntentKind.Event,
            JsonSerializer.Serialize(new { intent.SchemaId, payload = JsonElement.Parse(intent.Json) }, FeatureJson.Options)));
    }

    public void ProposeExternalEffect(ExternalEffectIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        AddIntent(new FeatureIntent(
            intent.LogicalOperationKey,
            FeatureIntentKind.ExternalEffect,
            JsonSerializer.Serialize(new
            {
                intent.CapabilityId,
                intent.ProviderConnectionId,
                payload = JsonElement.Parse(intent.Json)
            }, FeatureJson.Options)));
    }

    public async ValueTask<FeatureRunCommit> SealAsync(
        FeatureLeaseFence fence,
        CancellationToken cancellationToken = default)
    {
        Task? drain;
        lock (_gate)
        {
            ThrowIfClosed();
            _sealed = true;
            drain = _inFlight == 0
                ? null
                : (_drained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
        await _runLifetime.CancelAsync();
        if (drain is not null)
            await drain.WaitAsync(cancellationToken);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Commit(fence);
        }
    }

    public IDisposable Activate()
    {
        lock (_gate)
        {
            ThrowIfClosed();
            return FeatureRunScope.Enter(this);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? drain;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _sealed = true;
            drain = _inFlight == 0
                ? null
                : (_drained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
        await _runLifetime.CancelAsync();
        if (drain is not null)
            await drain;
        _runLifetime.Dispose();
    }

    internal async Task<T> QueryAsync<T>(
        string capabilityId,
        string? provider,
        string logicalOperationKey,
        object payload,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(
            capabilityId,
            provider,
            logicalOperationKey,
            JsonSerializer.SerializeToElement(payload, payload.GetType(), FeatureJson.Options),
            modelCall: false,
            cancellationToken);
        return result.Deserialize<T>(FeatureJson.Options)
            ?? throw new InvalidOperationException("The capability returned no result.");
    }

    internal void AddExternalProposal(string capabilityId, string provider, string operationKey, object payload)
    {
        _work.ProviderConnections.TryGetValue(provider, out var connection);
        ProposeExternalEffect(new ExternalEffectIntent(
            operationKey,
            capabilityId,
            connection == default ? null : connection.Value,
            JsonSerializer.Serialize(payload, payload.GetType(), FeatureJson.Options)));
    }

    private async Task<JsonElement> ExecuteAsync(
        string capabilityId,
        string? provider,
        string logicalOperationKey,
        JsonElement payload,
        bool modelCall,
        CancellationToken cancellationToken)
    {
        CancellationToken lifetimeToken;
        lock (_gate)
        {
            ThrowIfClosed();
            if (modelCall)
            {
                if (_modelCalls >= MaximumModelCalls)
                    throw new InvalidOperationException("A feature run cannot perform more than 4 model calls.");
                _modelCalls++;
            }
            else
            {
                if (_reads >= MaximumReads)
                    throw new InvalidOperationException("A feature run cannot perform more than 20 reads.");
                _reads++;
            }
            _inFlight++;
            lifetimeToken = _runLifetime.Token;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, cancellationToken);
        try
        {
            _work.ProviderConnections.TryGetValue(provider ?? string.Empty, out var connection);
            var now = _timeProvider.GetUtcNow();
            var deadline = _claim.LeaseExpiresAt < now.AddSeconds(30)
                ? _claim.LeaseExpiresAt
                : now.AddSeconds(30);
            return await _capabilities.ExecuteAsync(new CapabilityRequest(
                _work.OwnerId,
                _work.ActorId,
                _work.InstallationId,
                _claim.Release,
                _claim.Input.InputId,
                logicalOperationKey,
                capabilityId,
                1,
                connection == default ? null : connection,
                _work.GrantRevision,
                payload,
                deadline,
                _claim.Input.CorrelationId,
                _claim.Input.InputId), linked.Token);
        }
        finally
        {
            lock (_gate)
            {
                _inFlight--;
                if (_inFlight == 0)
                    _drained?.TrySetResult();
            }
        }
    }

    private void AddIntent(FeatureIntent intent)
    {
        lock (_gate)
        {
            ThrowIfClosed();
            if (_intents.Count >= MaximumIntents)
                throw new InvalidOperationException("A feature run cannot buffer more than 32 intents.");
            if (_intents.Any(existing => string.Equals(
                    existing.LogicalOperationKey,
                    intent.LogicalOperationKey,
                    StringComparison.Ordinal)))
                throw new InvalidOperationException("Feature intent operation keys must be unique within a run.");
            _intents.Add(intent);
        }
    }

    private FeatureRunCommit Commit(FeatureLeaseFence fence) => new(
        fence,
        _stateJson,
        _intents.ToArray(),
        new FeatureResourceUsage(_reads, _modelCalls),
        "{}");

    private void ThrowIfClosed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sealed)
            throw new InvalidOperationException("The feature run is sealed.");
    }
}

internal static class FeatureJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}

internal sealed class FeatureRunScope : IDisposable
{
    private static readonly AsyncLocal<CapabilityFeatureRunContext?> CurrentContext = new();
    private readonly CapabilityFeatureRunContext? _previous;
    private bool _disposed;

    private FeatureRunScope(CapabilityFeatureRunContext current)
    {
        _previous = CurrentContext.Value;
        CurrentContext.Value = current;
    }

    internal static CapabilityFeatureRunContext Current => CurrentContext.Value
        ?? throw new InvalidOperationException("A feature contract was used outside an active feature run.");

    internal static FeatureRunScope Enter(CapabilityFeatureRunContext current) => new(current);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CurrentContext.Value = _previous;
    }
}

public sealed class FeatureGmailMessageReader : IGmailMessageReader
{
    public Task<GmailMessage> ReadAsync(
        GmailMessageReadRequest request,
        CancellationToken cancellationToken = default) =>
        FeatureRunScope.Current.QueryAsync<GmailMessage>(
            GoogleCapabilityIds.GmailMessageRead,
            "google",
            FeatureRunScope.Current.Next("gmail-message-read"),
            request,
            cancellationToken);
}

public sealed class FeatureGmailMailboxReader : IGmailMailboxReader
{
    public Task<GmailMailboxPage> ReadAsync(
        GmailMailboxReadRequest request,
        CancellationToken cancellationToken = default) =>
        FeatureRunScope.Current.QueryAsync<GmailMailboxPage>(
            GoogleCapabilityIds.GmailMailboxRead,
            "google",
            FeatureRunScope.Current.Next("gmail-mailbox-read"),
            request,
            cancellationToken);
}

public sealed class FeatureGmailSendProposer : IGmailSendProposer
{
    public Task<GmailSendProposal> ProposeAsync(
        GmailSendProposalRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var proposal = new GmailSendProposal(
            request.Recipient,
            request.Subject,
            request.Body,
            request.LogicalOperationKey);
        FeatureRunScope.Current.AddExternalProposal(
            GoogleCapabilityIds.GmailSendPropose,
            "google",
            request.LogicalOperationKey,
            proposal);
        return Task.FromResult(proposal);
    }
}

public sealed class FeatureSalesforceRecordReader : ISalesforceRecordReader
{
    public Task<SalesforceRecord> ReadAsync(
        SalesforceRecordReadRequest request,
        CancellationToken cancellationToken = default) =>
        FeatureRunScope.Current.QueryAsync<SalesforceRecord>(
            SalesforceCapabilityIds.RecordRead,
            "salesforce",
            FeatureRunScope.Current.Next("salesforce-record-read"),
            request,
            cancellationToken);
}

public sealed class FeatureSalesforceUpdateProposer : ISalesforceUpdateProposer
{
    public Task<SalesforceUpdateProposal> ProposeAsync(
        SalesforceUpdateProposalRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var proposal = new SalesforceUpdateProposal(
            request.Record,
            request.Field,
            request.NewValue,
            request.LogicalOperationKey);
        FeatureRunScope.Current.AddExternalProposal(
            SalesforceCapabilityIds.RecordUpdatePropose,
            "salesforce",
            request.LogicalOperationKey,
            proposal);
        return Task.FromResult(proposal);
    }
}
