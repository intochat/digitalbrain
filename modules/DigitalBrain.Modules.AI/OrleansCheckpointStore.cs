using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Security;
using DigitalBrain.Tasks;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.AI;

[GenerateSerializer]
[Alias("ai.checkpoint-write")]
internal sealed record CheckpointWrite(
    [property: Id(0)] string SessionId,
    [property: Id(1)] byte[] ProtectedPayload,
    [property: Id(2)] WorkflowCheckpointReference? Parent);

[Alias("ai.workflow-checkpoint-grain")]
internal interface IWorkflowCheckpointGrain : IGrainWithStringKey
{
    [Alias("Create")]
    Task<WorkflowCheckpointReference> CreateAsync(CheckpointWrite command);

    [Alias("Read")]
    Task<byte[]> ReadAsync(WorkflowCheckpointReference checkpoint);

    [Alias("Index")]
    Task<WorkflowCheckpointReference[]> IndexAsync(WorkflowCheckpointReference? parent);
}

[GrainType("ai-workflow-checkpoint")]
internal sealed class WorkflowCheckpointGrain : DurableGrain, IWorkflowCheckpointGrain
{
    private const string PayloadsName = "ai.workflow-checkpoint.payloads";
    private const string ParentsName = "ai.workflow-checkpoint.parents";
    private const string OrderName = "ai.workflow-checkpoint.order";

    private readonly IDurableDictionary<string, byte[]> _payloads;
    private readonly IDurableDictionary<string, string> _parents;
    private readonly IDurableList<string> _order;

    public WorkflowCheckpointGrain()
    {
        _payloads = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<string, byte[]>>(PayloadsName);
        _parents = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<string, string>>(ParentsName);
        _order = ServiceProvider.GetRequiredKeyedService<IDurableList<string>>(OrderName);
    }

    public async Task<WorkflowCheckpointReference> CreateAsync(CheckpointWrite command)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireSession(command.SessionId);

        if (command.Parent is { } parent)
        {
            RequireSession(parent.SessionId);

            if (!_payloads.ContainsKey(parent.CheckpointId))
            {
                throw new InvalidOperationException(
                    $"Checkpoint parent '{parent.CheckpointId}' does not exist in session '{parent.SessionId}'.");
            }
        }

        var checkpointId = Guid.NewGuid().ToString("N");
        _payloads.Add(checkpointId, command.ProtectedPayload.ToArray());
        _parents.Add(checkpointId, command.Parent?.CheckpointId ?? string.Empty);
        _order.Add(checkpointId);
        await WriteStateAsync();

        return new WorkflowCheckpointReference(command.SessionId, checkpointId);
    }

    public Task<byte[]> ReadAsync(WorkflowCheckpointReference checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        RequireSession(checkpoint.SessionId);

        return Task.FromResult(_payloads.TryGetValue(checkpoint.CheckpointId, out var payload)
            ? payload.ToArray()
            : throw new InvalidOperationException(
                $"Checkpoint '{checkpoint.CheckpointId}' does not exist in session '{checkpoint.SessionId}'."));
    }

    public Task<WorkflowCheckpointReference[]> IndexAsync(WorkflowCheckpointReference? parent)
    {
        if (parent is not null)
        {
            RequireSession(parent.SessionId);
        }

        var expectedParent = parent?.CheckpointId ?? string.Empty;
        var session = ExpectedSession();
        var checkpoints = _order
            .Where(id => _parents.TryGetValue(id, out var storedParent)
                && string.Equals(storedParent, expectedParent, StringComparison.Ordinal))
            .Select(id => new WorkflowCheckpointReference(session, id))
            .ToArray();

        return Task.FromResult(checkpoints);
    }

    private void RequireSession(string sessionId)
    {
        if (!string.Equals(sessionId, ExpectedSession(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Checkpoint session '{sessionId}' does not match this stable checkpoint lineage.");
        }
    }

    private string ExpectedSession()
    {
        var key = this.GetPrimaryKeyString();
        var separator = key.LastIndexOf('/');
        var hash = separator >= 0 ? key[(separator + 1)..] : key;

        return $"dbw_{hash}";
    }
}

internal sealed class OrleansCheckpointStore(
    IWorkflowCheckpointGrain grain,
    string sessionId,
    IDurablePayloadProtector protector,
    string protectionPurpose) : JsonCheckpointStore
{
    internal string SessionId { get; } = sessionId;

    public override async ValueTask<CheckpointInfo> CreateCheckpointAsync(
        string sessionId,
        JsonElement checkpoint,
        CheckpointInfo? parent)
    {
        RequireSession(sessionId);
        var created = await grain.CreateAsync(new CheckpointWrite(
            sessionId,
            protector.Protect(
                protectionPurpose,
                Encoding.UTF8.GetBytes(checkpoint.GetRawText())),
            parent is null
                ? null
                : new WorkflowCheckpointReference(parent.SessionId, parent.CheckpointId)));

        return new CheckpointInfo(created.SessionId, created.CheckpointId);
    }

    public override async ValueTask<JsonElement> RetrieveCheckpointAsync(
        string sessionId,
        CheckpointInfo checkpoint)
    {
        RequireSession(sessionId);
        var protectedPayload = await grain.ReadAsync(
            new WorkflowCheckpointReference(checkpoint.SessionId, checkpoint.CheckpointId));
        var payload = protector.Unprotect(protectionPurpose, protectedPayload);
        using var document = JsonDocument.Parse(payload);

        return document.RootElement.Clone();
    }

    public override async ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(
        string sessionId,
        CheckpointInfo? parent)
    {
        RequireSession(sessionId);
        var checkpoints = await grain.IndexAsync(parent is null
            ? null
            : new WorkflowCheckpointReference(parent.SessionId, parent.CheckpointId));

        return checkpoints.Select(checkpoint => new CheckpointInfo(
            checkpoint.SessionId,
            checkpoint.CheckpointId));
    }

    private void RequireSession(string session)
    {
        if (!string.Equals(session, SessionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Checkpoint session '{session}' does not match expected session '{SessionId}'.");
        }
    }
}

internal sealed record WorkflowCheckpointIdentity(
    string GrainKey,
    string SessionId)
{
    internal static WorkflowCheckpointIdentity For(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        var source = Encoding.UTF8.GetBytes(
            $"v1\n{cursor.Worker}\n{cursor.Task}\n{cursor.Attempt.Value:D}");
        var hash = Convert.ToHexStringLower(SHA256.HashData(source));

        return new(
            $"{cursor.Worker.GrainKey}/workflow-checkpoint/{hash}",
            $"dbw_{hash}");
    }
}
