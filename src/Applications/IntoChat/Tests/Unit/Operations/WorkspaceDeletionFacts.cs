using IntoChat.Operations;
using IntoChat.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntoChat.Tests.Operations;

public sealed class WorkspaceDeletionFacts
{
    private sealed class RecordingVectorPurge : IWorkspaceVectorPurge
    {
        public List<string> Purged { get; } = [];

        public Task<long> PurgeAsync(string scopeId, CancellationToken ct)
        {
            Purged.Add(scopeId);
            return Task.FromResult(4L);
        }
    }

    private sealed class RecordingBackupPurge : IWorkspaceBackupPurge
    {
        public List<string> Purged { get; } = [];

        public Task PurgeAsync(string scopeId, CancellationToken ct)
        {
            Purged.Add(scopeId);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task DeletionPurgesVectorsAndBackupsForTheWorkspaceScope()
    {
        var vectors = new RecordingVectorPurge();
        var backups = new RecordingBackupPurge();
        var deletion = new WorkspaceDeletion(vectors, backups, NullLogger<WorkspaceDeletion>.Instance);
        var scope = WorkspaceScope.Create("owner", "data-workspace");

        await deletion.DeleteAsync(scope, TestContext.Current.CancellationToken);

        Assert.Equal(scope.Id, Assert.Single(vectors.Purged));
        Assert.Equal(scope.Id, Assert.Single(backups.Purged));
    }

    [Fact]
    public async Task HostedBackupPurgeRecordsWhenNoDelegateIsConfigured()
    {
        var purge = new HostedWorkspaceBackupPurge();
        await purge.PurgeAsync("workspace-abc", TestContext.Current.CancellationToken);
        Assert.Contains("workspace-abc", purge.Pending);
    }
}
