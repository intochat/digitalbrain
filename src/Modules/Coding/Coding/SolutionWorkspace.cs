using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Coding;

public sealed class SolutionWorkspace(ISolutionLoader loader, TimeProvider clock, ILogger<SolutionWorkspace> logger) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<Workspace> _retired = [];
    private Workspace? _workspace;
    private Task _pending = Task.CompletedTask;
    private WorkspaceStatus _status = WorkspaceStatus.NotOpened;
    private string? _solutionPath;
    private int _leases;
    private bool _disposed;

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

    public Solution? Current
    {
        get
        {
            lock (_gate)
            {
                return _workspace?.CurrentSolution;
            }
        }
    }

    public DateTimeOffset? ReadyAt { get; private set; }

    // Starts the load and returns at once; the returned task completes with the load and never faults.
    public Task BeginOpenAsync(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        lock (_gate)
        {
            if (_status.Phase == WorkspacePhase.Opening && string.Equals(_solutionPath, solutionPath, StringComparison.OrdinalIgnoreCase))
            {
                return _pending;
            }

            _solutionPath = solutionPath;
            _status = new WorkspaceStatus(WorkspacePhase.Opening, solutionPath, 0, 0, "starting");
            _pending = OpenCoreAsync(solutionPath, _lifetime.Token);
            return _pending;
        }
    }

    public Task BeginReloadAsync()
    {
        lock (_gate)
        {
            var path = _solutionPath ?? throw new WorkspaceNotReadyException(_status);
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

    public async Task<SymbolSearchResult> FindSymbolsAsync(SymbolSearch query, CancellationToken cancellationToken)
    {
        using var lease = Acquire();
        return await SolutionQueries.FindSymbolsAsync(lease.Solution, query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReferenceSearchResult> ReferencesAsync(ReferenceSearch query, CancellationToken cancellationToken)
    {
        using var lease = Acquire();
        return await SolutionQueries.ReferencesAsync(lease.Solution, query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DiagnosticsResult> DiagnosticsAsync(DiagnosticsQuery query, CancellationToken cancellationToken)
    {
        using var lease = Acquire();
        return await SolutionQueries.DiagnosticsAsync(lease.Solution, query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SolutionMap> MapAsync(MapQuery query, CancellationToken cancellationToken)
    {
        using var lease = Acquire();
        return await SolutionQueries.MapAsync(lease.Solution, lease.SolutionPath ?? string.Empty, query, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        Workspace? disposeNow = null;
        List<Workspace> retiredNow = [];
        bool pendingCompleted;
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
    }

    // Hands a query a stable Solution snapshot and keeps the workspace it came from alive until the query returns,
    // even if a reload swaps _workspace out from under it in the meantime.
    private Lease Acquire()
    {
        lock (_gate)
        {
            if (_status.Phase != WorkspacePhase.Ready || _workspace is not { } workspace)
            {
                throw new WorkspaceNotReadyException(_status);
            }

            _leases++;
            return new Lease(this, workspace.CurrentSolution, _solutionPath);
        }
    }

    private void Release()
    {
        lock (_gate)
        {
            _leases--;
            if (_leases != 0)
            {
                return;
            }

            foreach (var workspace in _retired)
            {
                workspace.Dispose();
            }

            _retired.Clear();
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
                }
                else
                {
                    var previous = _workspace;
                    _workspace = loaded.Workspace;
                    _status = new WorkspaceStatus(WorkspacePhase.Ready, solutionPath, solution.ProjectIds.Count, documents, detail);
                    ReadyAt = clock.GetUtcNow();
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
                _status = new WorkspaceStatus(WorkspacePhase.Failed, solutionPath, 0, 0, "the load was cancelled");
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
    }

    private readonly struct Lease(SolutionWorkspace owner, Solution solution, string? solutionPath) : IDisposable
    {
        public Solution Solution { get; } = solution;

        public string? SolutionPath { get; } = solutionPath;

        public void Dispose() => owner.Release();
    }
}
