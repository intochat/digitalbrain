using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Coding;

public sealed class SolutionWorkspace(ISolutionLoader loader, ILogger<SolutionWorkspace> logger) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _queryGate = new(2, 2);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly List<Workspace> _retired = [];
    private Workspace? _workspace;
    private Task _pending = Task.CompletedTask;
    private WorkspaceStatus _status = WorkspaceStatus.NotOpened;
    private string? _solutionPath;
    private int _leases;
    private int _reloadReasons;
    private bool _disposed;
    private long _snapshotVersion;

    public WorkspaceStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    // Design section 4.2: semantic queries run at most two at a time.
    internal int AvailableQuerySlots => _queryGate.CurrentCount;

    public long SnapshotVersion => Interlocked.Read(ref _snapshotVersion);

    internal Action<string>? Opened { get; set; }

    // Starts the load and returns at once; the returned task completes with the load, never faults, and
    // answers true only when this call is what started a load. The grain's own open after a warmup (or a
    // second open of the path already loaded) answers false so it can record a repeat without reloading.
    public Task<bool> BeginOpenAsync(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        lock (_gate)
        {
            if (_status.Phase is WorkspacePhase.Opening or WorkspacePhase.Ready && string.Equals(_solutionPath, solutionPath, StringComparison.OrdinalIgnoreCase))
            {
                return WhenLoaded(_pending, started: false);
            }

            _solutionPath = solutionPath;
            _reloadReasons = 0;
            _status = new WorkspaceStatus(WorkspacePhase.Opening, solutionPath, 0, 0, "starting");
            _pending = OpenCoreAsync(solutionPath, _lifetime.Token);
            return WhenLoaded(_pending, started: true);
        }

        static async Task<bool> WhenLoaded(Task load, bool started)
        {
            await load.ConfigureAwait(false);
            return started;
        }
    }

    public Task BeginReloadAsync()
    {
        lock (_gate)
        {
            var path = _solutionPath ?? throw new WorkspaceNotReadyException(_status);
            _reloadReasons = 0;
            _status = new WorkspaceStatus(WorkspacePhase.Opening, path, 0, 0, "reloading");
            _pending = OpenCoreAsync(path, _lifetime.Token);
            return _pending;
        }
    }

    public async Task WhenReadyAsync(CancellationToken cancellationToken)
    {
        Task pending;
        lock (_gate)
        {
            pending = _pending;
        }

        await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (Status.Phase != WorkspacePhase.Ready)
        {
            throw new WorkspaceNotReadyException(Status);
        }
    }

    public async Task<T> QueryAsync<T>(Func<Solution, CancellationToken, Task<T>> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        return await query(lease.Solution, cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> QueryAsync<T>(Func<Solution, string, CancellationToken, Task<T>> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        return await query(lease.Solution, lease.SolutionPath ?? string.Empty, cancellationToken).ConfigureAwait(false);
    }

    // One lease around the whole transaction: the change is computed on the leased snapshot and applied
    // through TryApplyChanges, which refuses a snapshot the workspace has moved past so a fold or a reload
    // between check and commit cannot be overwritten. TryApplyChanges may write the changed documents to disk
    // itself -- MSBuildWorkspace does -- so the before/after comparison below reports a path as written
    // whenever its text actually changed, whichever of the two wrote it.
    // The writer gate is taken before the lease so the snapshot a change is computed on is the snapshot it
    // is applied to: two writers that only queue on TryApplyChanges would refuse the second as stale even
    // when the two touch different files. Readers keep the two-slot query gate and never wait on this one.
    public async Task<CommitOutcome> CommitAsync(Func<Solution, CancellationToken, Task<Solution>> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await CommitCoreAsync(change, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<CommitOutcome> CommitCoreAsync(Func<Solution, CancellationToken, Task<Solution>> change, CancellationToken cancellationToken)
    {
        using var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        var changed = await change(lease.Solution, cancellationToken).ConfigureAwait(false);
        var projectChanges = changed.GetChanges(lease.Solution).GetProjectChanges().ToArray();
        if (projectChanges.Any(static projectChange => projectChange.GetAddedDocuments().Any() || projectChange.GetRemovedDocuments().Any()))
        {
            throw new InvalidOperationException("This change set adds or removes documents; phase 1 commits only edits to existing files.");
        }

        // Every document's path is resolved before TryApplyChanges runs, so a document with no file path
        // refuses the whole commit before anything is applied or written, not partway through.
        var documents = projectChanges
            .SelectMany(project => project.GetChangedDocuments().Select(id => changed.GetDocument(id)!))
            .OrderBy(static document => document.FilePath, StringComparer.Ordinal)
            .Select(document => (Document: document, Path: document.FilePath ?? throw new InvalidOperationException($"Document '{document.Name}' has no file path to write to.")))
            .ToArray();

        // Snapshotted before TryApplyChanges, which may overwrite these files itself: "did this path change"
        // has to be judged against the text on disk before the apply ran, not against disk afterward.
        var beforeTexts = new string?[documents.Length];
        for (var i = 0; i < documents.Length; i++)
        {
            var path = documents[i].Path;
            beforeTexts[i] = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false) : null;
        }

        if (!lease.Workspace.TryApplyChanges(changed))
        {
            throw new InvalidOperationException("The workspace changed while the change set was being checked. Check it again before committing.");
        }

        var written = new List<string>();
        for (var i = 0; i < documents.Length; i++)
        {
            var (document, path) = documents[i];
            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var content = text.ToString();
            if (string.Equals(beforeTexts[i], content, StringComparison.Ordinal))
            {
                continue;
            }

            if (!File.Exists(path) || !string.Equals(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), content, StringComparison.Ordinal))
            {
                var encoding = text.Encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                await File.WriteAllTextAsync(path, content, encoding, cancellationToken).ConfigureAwait(false);
            }

            written.Add(path);
        }

        return new CommitOutcome(written, Interlocked.Increment(ref _snapshotVersion));
    }

    // Folds an external save of one document into the snapshot; false when the path is not a document or
    // the text is unchanged (our own commit writes come back through the watcher and must not bump anything).
    public async Task<bool> FoldAsync(string path, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);
        if (Status.Phase != WorkspacePhase.Ready)
        {
            return false;
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await FoldCoreAsync(path, text, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<bool> FoldCoreAsync(string path, string text, CancellationToken cancellationToken)
    {
        using var lease = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        var ids = lease.Solution.GetDocumentIdsWithFilePath(path);
        if (ids.IsDefaultOrEmpty)
        {
            // MSBuildWorkspace registers documents with the platform separator; the adhoc fixture uses forward slashes.
            ids = lease.Solution.GetDocumentIdsWithFilePath(path.Replace('/', Path.DirectorySeparatorChar));
        }

        if (ids.IsDefaultOrEmpty)
        {
            return false;
        }

        var changed = lease.Solution;
        foreach (var id in ids)
        {
            var current = await changed.GetDocument(id)!.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(current.ToString(), text, StringComparison.Ordinal))
            {
                changed = changed.WithDocumentText(id, SourceText.From(text));
            }
        }

        if (ReferenceEquals(changed, lease.Solution) || !lease.Workspace.TryApplyChanges(changed))
        {
            return false;
        }

        Interlocked.Increment(ref _snapshotVersion);
        return true;
    }

    // The reason is bounded: the path that asked for the reload last, plus how many others asked before it.
    // A rebuild or a branch switch can touch hundreds of project files, and a concatenated detail would grow
    // without limit in a status every read carries. The count resets when the next load lands.
    public void MarkReloadNeeded(string path)
    {
        lock (_gate)
        {
            if (_status.Phase != WorkspacePhase.Ready)
            {
                return;
            }

            _reloadReasons++;
            var message = $"reload needed: {path}";
            _status = _status with { ReloadNeeded = true, Detail = _reloadReasons == 1 ? message : $"{message} (+{_reloadReasons - 1} more)" };
        }
    }

    public Task<SymbolSearchResult> FindSymbolsAsync(SymbolSearch query, CancellationToken cancellationToken)
        => QueryAsync((solution, token) => SolutionQueries.FindSymbolsAsync(solution, query, token), cancellationToken);

    public Task<ReferenceSearchResult> ReferencesAsync(ReferenceSearch query, CancellationToken cancellationToken)
        => QueryAsync((solution, token) => SolutionQueries.ReferencesAsync(solution, query, token), cancellationToken);

    public Task<DiagnosticsResult> DiagnosticsAsync(DiagnosticsQuery query, CancellationToken cancellationToken)
        => QueryAsync((solution, token) => SolutionQueries.DiagnosticsAsync(solution, query, token), cancellationToken);

    public Task<SolutionMap> MapAsync(MapQuery query, CancellationToken cancellationToken)
        => QueryAsync((solution, solutionPath, token) => SolutionQueries.MapAsync(solution, solutionPath, query, token), cancellationToken);

    public Task<Skeleton> SkeletonAsync(SkeletonQuery query, CancellationToken cancellationToken)
        => QueryAsync((solution, token) => SolutionQueries.SkeletonAsync(solution, query, token), cancellationToken);

    public Task<MemberSource> MemberAsync(MemberQuery query, CancellationToken cancellationToken)
        => QueryAsync((solution, token) => SolutionQueries.MemberAsync(solution, query, token), cancellationToken);

    public Task<CallersResult> CallersAsync(CallersQuery query, CancellationToken cancellationToken)
        => QueryAsync((solution, token) => SolutionQueries.CallersAsync(solution, query, token), cancellationToken);

    public Task<SymbolSearchResult> ImplementationsAsync(ImplementationsQuery query, CancellationToken cancellationToken)
        => QueryAsync((solution, token) => SolutionQueries.ImplementationsAsync(solution, query, token), cancellationToken);

    public Task<SymbolSearchResult> DerivedAsync(DerivedQuery query, CancellationToken cancellationToken)
        => QueryAsync((solution, token) => SolutionQueries.DerivedAsync(solution, query, token), cancellationToken);

    public void Dispose()
    {
        Workspace? disposeNow = null;
        List<Workspace> retiredNow = [];
        bool pendingCompleted;
        bool disposeQueryGate;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var current = _workspace;
            _workspace = null;
            if (_leases == 0)
            {
                disposeNow = current;
                retiredNow = [.. _retired];
                _retired.Clear();
            }
            else if (current is not null)
            {
                // A query is still reading the current workspace; Release() disposes the retired list once it drops to zero.
                _retired.Add(current);
            }

            _status = new WorkspaceStatus(WorkspacePhase.Failed, _solutionPath, 0, 0, "the workspace was disposed");
            pendingCompleted = _pending.IsCompleted;
            disposeQueryGate = _leases == 0;
        }

        _lifetime.Cancel();
        disposeNow?.Dispose();
        foreach (var workspace in retiredNow)
        {
            workspace.Dispose();
        }

        if (pendingCompleted)
        {
            _lifetime.Dispose();
        }

        if (disposeQueryGate)
        {
            _queryGate.Dispose();
        }

        // CommitAsync/FoldAsync release this gate from their own finally after the inner query lease is
        // already gone, so disposing it from Release() (or unconditionally here) can race that release and
        // throw ObjectDisposedException, masking a commit that otherwise succeeded. Claiming it with a
        // zero-timeout wait only disposes it when no writer currently holds it; a writer in flight simply
        // keeps it (a SemaphoreSlim that never touches AvailableWaitHandle holds no unmanaged resource).
        if (_writeGate.Wait(0))
        {
            _writeGate.Dispose();
        }
    }

    // Hands a query a stable Solution snapshot and keeps the workspace it came from alive until the query returns,
    // even if a reload swaps _workspace out from under it in the meantime. Bounded to two concurrent queries (design 4.2).
    private async Task<Lease> AcquireAsync(CancellationToken cancellationToken)
    {
        await _queryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (_status.Phase != WorkspacePhase.Ready || _workspace is not { } workspace)
                {
                    throw new WorkspaceNotReadyException(_status);
                }

                _leases++;
                return new Lease(this, workspace, workspace.CurrentSolution, _solutionPath);
            }
        }
        catch
        {
            _queryGate.Release();
            throw;
        }
    }

    private void Release()
    {
        bool disposeQueryGate;
        lock (_gate)
        {
            _leases--;
            if (_leases == 0)
            {
                foreach (var workspace in _retired)
                {
                    workspace.Dispose();
                }

                _retired.Clear();
            }

            disposeQueryGate = _disposed && _leases == 0;
        }

        _queryGate.Release();
        if (disposeQueryGate)
        {
            _queryGate.Dispose();
        }
    }

    private async Task OpenCoreAsync(string solutionPath, CancellationToken cancellationToken)
    {
        // Yield so the caller's lock is released before any loader work runs.
        await Task.Yield();
        var progress = new Progress<string>(detail =>
        {
            lock (_gate)
            {
                if (_status.Phase == WorkspacePhase.Opening)
                {
                    _status = _status with { Detail = detail };
                }
            }
        });
        Workspace? disposeNow = null;
        var disposeLifetime = false;
        var opened = false;
        try
        {
            var loaded = await loader.OpenAsync(solutionPath, progress, cancellationToken).ConfigureAwait(false);
            var solution = loaded.Workspace.CurrentSolution;
            var documents = solution.Projects.Sum(static project => project.DocumentIds.Count);
            var detail = loaded.Failures.Count == 0 ? null : $"{loaded.Failures.Count} load failures; first: {loaded.Failures[0]}";
            lock (_gate)
            {
                if (_disposed)
                {
                    loaded.Workspace.Dispose();
                    _status = new WorkspaceStatus(WorkspacePhase.Failed, solutionPath, 0, 0, "the workspace was disposed");
                    // Dispose() ran before this late load landed; it left _lifetime alive for us to dispose here.
                    disposeLifetime = true;
                }
                else
                {
                    var previous = _workspace;
                    _workspace = loaded.Workspace;
                    _reloadReasons = 0;
                    _status = new WorkspaceStatus(WorkspacePhase.Ready, solutionPath, solution.ProjectIds.Count, documents, detail, ReloadNeeded: false);
                    opened = true;
                    if (previous is not null)
                    {
                        if (_leases == 0)
                        {
                            disposeNow = previous;
                        }
                        else
                        {
                            _retired.Add(previous);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    _status = new WorkspaceStatus(WorkspacePhase.Failed, solutionPath, 0, 0, "the load was cancelled");
                }

                disposeLifetime = _disposed;
            }
        }
#pragma warning disable CA1031 // A load failure becomes the Failed status, not a crash; every exception is caught deliberately here.
        catch (Exception error)
#pragma warning restore CA1031
        {
            logger.LogError(error, "Opening {SolutionPath} failed.", solutionPath);
            lock (_gate)
            {
                _status = new WorkspaceStatus(WorkspacePhase.Failed, solutionPath, 0, 0, error.Message);
            }
        }

        disposeNow?.Dispose();
        if (disposeLifetime)
        {
            _lifetime.Dispose();
        }

        if (opened)
        {
#pragma warning disable CA1031 // BeginOpenAsync's task must never fault: a bad Opened callback is logged, not thrown.
            try
            {
                Opened?.Invoke(solutionPath);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "The opened callback failed for {SolutionPath}.", solutionPath);
            }
#pragma warning restore CA1031
        }
    }

    private readonly struct Lease(SolutionWorkspace owner, Workspace workspace, Solution solution, string? solutionPath) : IDisposable
    {
        public Workspace Workspace { get; } = workspace;

        public Solution Solution { get; } = solution;

        public string? SolutionPath { get; } = solutionPath;

        public void Dispose() => owner.Release();
    }
}
