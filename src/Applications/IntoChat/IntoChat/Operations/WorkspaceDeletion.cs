using System.Collections.Concurrent;
using DigitalBrain.Contracts;
using DigitalBrain.Memory;
using IntoChat.Workspace;
using Microsoft.Extensions.Logging;

namespace IntoChat.Operations;

// The deletion hook: a workspace (or account) deletion must purge every derived copy of its data.
// Vectors live in the Memory module's Qdrant collection; backups are snapshots held by the hosting
// storage account. Both are purged through this one seam so no deletion path can skip a copy.
internal interface IWorkspaceVectorPurge
{
    Task<long> PurgeAsync(string scopeId, CancellationToken ct);
}

internal interface IWorkspaceBackupPurge
{
    Task PurgeAsync(string scopeId, CancellationToken ct);
}

internal static class WorkspaceMemoryNamespace
{
    public const string Name = "intochat.workspace";
}

internal sealed class MemoryWorkspaceVectorPurge(IDigitalBrain brain) : IWorkspaceVectorPurge
{
    public async Task<long> PurgeAsync(string scopeId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        var memory = brain.Get<IMemory>(scopeId);
        return await memory.PurgeNamespace(new(WorkspaceMemoryNamespace.Name)).WaitAsync(ct);
    }
}

// Config-only backup purge. When the hosting profile provides a purge delegate it runs it; otherwise
// the request is recorded so the operations runbook's retention job can pick it up. Never calls Azure.
internal sealed class HostedWorkspaceBackupPurge(Func<string, CancellationToken, Task>? purge = null) : IWorkspaceBackupPurge
{
    private readonly ConcurrentQueue<string> _pending = new();

    public IReadOnlyCollection<string> Pending => _pending.ToArray();

    public async Task PurgeAsync(string scopeId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        if (purge is not null)
        {
            await purge(scopeId, ct).WaitAsync(ct);
            return;
        }

        _pending.Enqueue(scopeId);
    }
}

internal sealed class WorkspaceDeletion(
    IWorkspaceVectorPurge vectors,
    IWorkspaceBackupPurge backups,
    ILogger<WorkspaceDeletion> logger)
{
    public async Task DeleteAsync(WorkspaceScope scope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var removed = await vectors.PurgeAsync(scope.Id, ct).ConfigureAwait(false);
        await backups.PurgeAsync(scope.Id, ct).ConfigureAwait(false);
        logger.LogInformation("Workspace deletion purged {RemovedVectors} vectors and queued backup purge.", removed);
    }
}
