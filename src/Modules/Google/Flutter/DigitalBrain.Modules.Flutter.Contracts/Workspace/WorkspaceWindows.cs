namespace DigitalBrain.Flutter.Workspace;

public static class WorkspaceWindows
{
    private const int MaxAttempts = 8;

    // Opens a surface window unless it is already open on the same reference.
    public static async Task EnsureOpenAsync(this IWorkspace workspace, string windowId, string title, WindowReference reference, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var state = await workspace.Read().WaitAsync(ct);
        if (state.Windows.Any(window => window.Id == windowId && window.IsOpen && window.Reference == reference)) { return; }
        await RetryOnConflictAsync(state.Revision, revision =>
            workspace.OpenSurface(new(Guid.NewGuid().ToString(), windowId, title, reference, revision)), ct);
    }

    // A conflict carries the current revision, so the next attempt needs no extra read.
    public static async Task<T> RetryOnConflictAsync<T>(long expectedRevision, Func<long, Task<T>> attempt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        for (var tries = 1; ; tries++)
        {
            ct.ThrowIfCancellationRequested();
            try { return await attempt(expectedRevision).WaitAsync(ct); }
            catch (WorkspaceRevisionConflictException conflict) when (tries < MaxAttempts) { expectedRevision = conflict.CurrentRevision; }
        }
    }
}
