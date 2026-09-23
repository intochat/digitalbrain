using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Coding;
using DigitalBrain.Core;

namespace DigitalBrain.Behavior;

internal sealed class BehaviorProgramStore(IDocumentStore<BehaviorProgramDocument> documents)
{
    private readonly IDocumentStore<BehaviorProgramDocument> _documents = documents;
    public Task<BehaviorSnapshot> ReadAsync(string id, CancellationToken ct) => _documents.ReadAsync(id, d => d.Snapshot, ct);
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken ct)
    {
        var live = new List<string>();
        foreach (var id in await _documents.ListIdsAsync(ct).ConfigureAwait(false))
        {
            if (!await _documents.ReadAsync(id, d => d.Deleted, ct).ConfigureAwait(false)) { live.Add(id); }
        }
        return live;
    }
    public Task<bool> CanStartAsync(string id, CancellationToken ct)
        => _documents.ReadAsync(id, d => d.Retries <= 3 && (d.NextRetryAt is null || d.NextRetryAt <= DateTimeOffset.UtcNow), ct);
    public Task<DateTimeOffset?> PendingRetryAsync(string id, CancellationToken ct)
        => _documents.ReadAsync(id, d => d.Retries <= 3 && d.NextRetryAt > DateTimeOffset.UtcNow ? d.NextRetryAt : null, ct);
    public Task<TResult> UpdateAsync<TResult>(string id, Func<BehaviorProgramDocument, TResult> action, CancellationToken ct)
        => _documents.UpdateAsync(id, action, ct);

    public Task<BehaviorSnapshot?> ReplayAsync<T>(string id, string kind, Guid operation, T request, CancellationToken ct)
        => _documents.ReadAsync(id, d =>
        {
            if (!d.Operations.TryGetValue(operation, out var prior)) { return null; }
            if (prior.Hash != RequestHash(kind, request)) { throw new InvalidOperationException("Operation ID was used with different input."); }
            return prior.Snapshot;
        }, ct);

    private static string RequestHash<T>(string kind, T request)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { kind, request })));

    public Task<BehaviorSnapshot> DeployAsync(string id, DeployBehavior request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateConfiguration(request.ConfigurationJson);
        return Command(id, "deploy", request.ExpectedRevision, request.OperationId, request, d =>
        {
            if (d.Snapshot.Deployments.Count >= 128) { throw new InvalidOperationException("Deployment retention limit reached; create a new program."); }
            var deployment = new BehaviorDeployment(d.Snapshot.Deployments.Count + 1, request.Artifact, request.ConfigurationJson, DateTimeOffset.UtcNow, request.AgentRunId);
            d.Retries = 0;
            d.NextRetryAt = null;
            return d.Snapshot with
            {
                Revision = d.Snapshot.Revision + 1,
                DesiredState = BehaviorDesiredState.Running,
                State = BehaviorExecutionState.Starting,
                DesiredDeploymentRevision = deployment.Revision,
                GenerationId = null,
                Ready = false,
                Error = null,
                Deployments = [.. d.Snapshot.Deployments, deployment]
            };
        }, ct);
    }

    public Task<BehaviorSnapshot> ChangeAsync(string id, ChangeBehaviorState request, bool running, CancellationToken ct)
        => Command(id, running ? "start" : "stop", request.ExpectedRevision, request.OperationId, request, d =>
        {
            if (running && d.Snapshot.DesiredDeploymentRevision is null) { throw new InvalidOperationException("Deploy an artifact before starting."); }
            d.Retries = 0;
            d.NextRetryAt = null;
            return d.Snapshot with
            {
                Revision = d.Snapshot.Revision + 1,
                DesiredState = running ? BehaviorDesiredState.Running : BehaviorDesiredState.Stopped,
                State = running ? BehaviorExecutionState.Starting : BehaviorExecutionState.Stopping,
                Ready = false,
                GenerationId = null,
                Error = null
            };
        }, ct);

    public Task<BehaviorSnapshot> RollbackAsync(string id, RollbackBehavior request, CancellationToken ct)
        => Command(id, "rollback", request.ExpectedRevision, request.OperationId, request, d =>
        {
            if (d.Snapshot.Deployments.Count >= 128) { throw new InvalidOperationException("Deployment retention limit reached."); }
            var previous = d.Snapshot.Deployments.SingleOrDefault(x => x.Revision == request.DeploymentRevision)
                ?? throw new KeyNotFoundException("Retained deployment not found.");
            var deployment = previous with { Revision = d.Snapshot.Deployments.Count + 1, CreatedAt = DateTimeOffset.UtcNow };
            d.Retries = 0;
            d.NextRetryAt = null;
            return d.Snapshot with
            {
                Revision = d.Snapshot.Revision + 1,
                DesiredState = BehaviorDesiredState.Running,
                State = BehaviorExecutionState.Starting,
                DesiredDeploymentRevision = deployment.Revision,
                GenerationId = null,
                Ready = false,
                Error = null,
                Deployments = [.. d.Snapshot.Deployments, deployment]
            };
        }, ct);

    public Task<BehaviorSnapshot> RemoveAsync(string id, DeleteBehavior request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.OperationId == Guid.Empty) { throw new ArgumentException("Operation ID is required.", nameof(request.OperationId)); }
        var hash = RequestHash("delete", request);
        return _documents.UpdateAsync(id, d =>
        {
            if (d.Operations.TryGetValue(request.OperationId, out var prior))
            {
                if (prior.Hash != hash) { throw new InvalidOperationException("Operation ID was used with different input."); }
                return prior.Snapshot;
            }
            if (d.Snapshot.Revision != request.ExpectedRevision) { throw new InvalidOperationException("Behavior revision conflict; read the program again."); }
            d.Id = id;
            d.Deleted = true;
            d.Snapshot = d.Snapshot with
            {
                Revision = d.Snapshot.Revision + 1,
                DesiredState = BehaviorDesiredState.Stopped,
                State = BehaviorExecutionState.Stopping,
                Ready = false,
                GenerationId = null,
                Error = null
            };
            d.Operations.Add(request.OperationId, new(hash, d.Snapshot));
            return d.Snapshot;
        }, ct);
    }

    private Task<BehaviorSnapshot> Command<T>(string id, string kind, long revision, Guid operation, T request,
        Func<BehaviorProgramDocument, BehaviorSnapshot> apply, CancellationToken ct)
    {
        if (operation == Guid.Empty) { throw new ArgumentException("Operation ID is required.", nameof(operation)); }
        var hash = RequestHash(kind, request);
        return _documents.UpdateAsync(id, d =>
        {
            if (d.Operations.TryGetValue(operation, out var prior))
            {
                if (prior.Hash != hash) { throw new InvalidOperationException("Operation ID was used with different input."); }
                return prior.Snapshot;
            }
            if (d.Snapshot.Revision != revision) { throw new InvalidOperationException("Behavior revision conflict; read the program again."); }
            if (d.Operations.Count >= 1024) { throw new InvalidOperationException("Operation retention limit reached; create a new program."); }
            d.Id = id;
            d.Snapshot = apply(d);
            d.Operations.Add(operation, new(hash, d.Snapshot));
            return d.Snapshot;
        }, ct);
    }

    internal static void ValidateConfiguration(string configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (Encoding.UTF8.GetByteCount(configuration) > 32768) { throw new ArgumentException("Behavior configuration exceeds 32 KiB.", nameof(configuration)); }
        using var document = JsonDocument.Parse(configuration);
        if (document.RootElement.ValueKind != JsonValueKind.Object) { throw new ArgumentException("Configuration must be a JSON object.", nameof(configuration)); }
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String || !property.Name.StartsWith("Behavior__", StringComparison.Ordinal)
                || property.Name.Contains('=') || property.Name.Contains('\0') || property.Value.GetString()!.Contains('\0'))
            { throw new ArgumentException("Use nonsecret string configuration keys prefixed with Behavior__.", nameof(configuration)); }
        }
    }
}

internal sealed class BehaviorProgramDocument
{
    public string Id { get; set; } = "";
    public bool Deleted { get; set; }
    public BehaviorSnapshot Snapshot { get; set; } = new(0, BehaviorDesiredState.Stopped, BehaviorExecutionState.Stopped, null, null, null, false, null, []);
    public Dictionary<Guid, BehaviorCommandReceipt> Operations { get; set; } = [];
    public int Retries { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
}

internal sealed record BehaviorCommandReceipt(string Hash, BehaviorSnapshot Snapshot);