using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter.Workspace.Signals;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Workspace;

[GrainType("ui.workspace")]
internal sealed class WorkspaceNeuron(
    [PersistentState("workspace", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<WorkspaceStorage> store)
    : Neuron, IWorkspace
{
    public async Task<WorkspaceState> Open(OpenWindow request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request.OperationId, 256, nameof(request.OperationId));
        Validate(request.WindowId, 256, nameof(request.WindowId));
        Validate(request.Title, 200, nameof(request.Title));
        ArgumentNullException.ThrowIfNull(request.View);
        Validate(request.View.Id, 256, nameof(request.View));
        var current = store.State;
        if (current.Receipts.TryGetValue(request.OperationId, out var previous))
        {
            if (previous.WindowId != request.WindowId || previous.Title != request.Title || previous.View != request.View)
            { throw new InvalidOperationException("This operation ID already belongs to a different window request."); }
            return Snapshot();
        }
        if (current.SurfaceReceipts.ContainsKey(request.OperationId)) { throw new InvalidOperationException("This operation ID belongs to a surface request."); }
        RequireRevision(request.ExpectedRevision);
        var windows = new Dictionary<string, WorkspaceWindow>(current.Windows, StringComparer.Ordinal);
        if (windows.TryGetValue(request.WindowId, out var existing) && (existing.View != request.View || existing.Surface is not null))
        { throw new InvalidOperationException("A window ID cannot be reassigned to another table."); }
        windows[request.WindowId] = new(request.WindowId, request.Title, request.View, true);
        var receipts = new Dictionary<string, OpenWindow>(current.Receipts, StringComparer.Ordinal) { [request.OperationId] = request };
        await Save(new() { Revision = checked(current.Revision + 1), Windows = windows, Receipts = receipts, SurfaceReceipts = current.SurfaceReceipts });
        return Snapshot();
    }

    public async Task<WorkspaceState> OpenSurface(OpenSurfaceWindow request)
    {
        Validate(request.OperationId, 256, nameof(request.OperationId));
        Validate(request.WindowId, 256, nameof(request.WindowId));
        Validate(request.Title, 200, nameof(request.Title));
        ArgumentNullException.ThrowIfNull(request.Surface);
        if (request.Surface.Kind != UIVocabulary.SurfaceType || !request.Surface.Name.StartsWith(this.GetPrimaryKeyString() + "/", StringComparison.Ordinal))
        { throw new ArgumentException("A workspace window must reference a surface in this workspace."); }
        var current = store.State;
        if (current.SurfaceReceipts.TryGetValue(request.OperationId, out var previous))
        {
            if (previous.WindowId != request.WindowId || previous.Title != request.Title || previous.Surface != request.Surface)
            { throw new InvalidOperationException("This operation ID already belongs to another surface request."); }
            return Snapshot();
        }
        if (current.Receipts.ContainsKey(request.OperationId)) { throw new InvalidOperationException("This operation ID belongs to a table request."); }
        RequireRevision(request.ExpectedRevision);
        if (current.Windows.TryGetValue(request.WindowId, out var existing) && existing.Surface is null)
        { throw new InvalidOperationException("A table window cannot be changed into an app window."); }
        var windows = new Dictionary<string, WorkspaceWindow>(current.Windows, StringComparer.Ordinal)
        { [request.WindowId] = new(request.WindowId, request.Title, new(""), true, request.Surface) };
        await Save(new()
        {
            Revision = checked(current.Revision + 1),
            Windows = windows,
            Receipts = current.Receipts,
            SurfaceReceipts = new(current.SurfaceReceipts) { [request.OperationId] = request }
        });
        return Snapshot();
    }

    public async Task<WorkspaceState> Close(string windowId, long expectedRevision)
    {
        Validate(windowId, 256, nameof(windowId));
        RequireRevision(expectedRevision);
        if (!store.State.Windows.TryGetValue(windowId, out var window))
        { throw new KeyNotFoundException("The workspace has no such window."); }
        if (!window.IsOpen) { return Snapshot(); }
        var windows = new Dictionary<string, WorkspaceWindow>(store.State.Windows, StringComparer.Ordinal)
        {
            [windowId] = window with { IsOpen = false },
        };
        await Save(new() { Revision = checked(store.State.Revision + 1), Windows = windows, Receipts = store.State.Receipts, SurfaceReceipts = store.State.SurfaceReceipts });
        return Snapshot();
    }

    public Task<WorkspaceState> Read() => Task.FromResult(Snapshot());
    private WorkspaceState Snapshot() => new(store.State.Revision, store.State.Windows.Values.ToArray());

    private async Task Save(WorkspaceStorage next)
    {
        var previous = store.State;
        store.State = next;
        try { await store.WriteStateAsync(); }
        catch { store.State = previous; throw; }
        await PublishAsync(new WorkspaceChanged(this.GetPrimaryKeyString(), next.Revision));
    }
    private void RequireRevision(long revision)
    {
        if (store.State.Revision != revision)
        { throw new WorkspaceRevisionConflictException($"Expected revision {revision}; current revision is {store.State.Revision}."); }
    }
    private static void Validate(string value, int limit, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > limit)
        { throw new ArgumentException($"Provide {name} with 1–{limit} characters.", name); }
    }
}

[GenerateSerializer]
internal sealed class WorkspaceStorage
{
    [Id(0)] public long Revision { get; set; }
    [Id(1)] public Dictionary<string, WorkspaceWindow> Windows { get; set; } = new(StringComparer.Ordinal);
    [Id(3)] public Dictionary<string, OpenSurfaceWindow> SurfaceReceipts { get; set; } = new(StringComparer.Ordinal);
    [Id(2)] public Dictionary<string, OpenWindow> Receipts { get; set; } = new(StringComparer.Ordinal);
}