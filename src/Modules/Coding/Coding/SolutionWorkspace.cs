using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Coding;

public sealed class SolutionWorkspace(ISolutionLoader loader, TimeProvider clock, ILogger<SolutionWorkspace> logger) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Workspace? _workspace;
    private Task _pending = Task.CompletedTask;
    private WorkspaceStatus _status = WorkspaceStatus.NotOpened;
    private string? _solutionPath;

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

    public Solution? Current => _workspace?.CurrentSolution;

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
        var path = _solutionPath ?? throw new WorkspaceNotReadyException(Status);
        lock (_gate)
        {
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

    public Task<SymbolSearchResult> FindSymbolsAsync(SymbolSearch query, CancellationToken cancellationToken)
        => SolutionQueries.FindSymbolsAsync(Ready(), query, cancellationToken);

    public Task<ReferenceSearchResult> ReferencesAsync(ReferenceSearch query, CancellationToken cancellationToken)
        => SolutionQueries.ReferencesAsync(Ready(), query, cancellationToken);

    public Task<DiagnosticsResult> DiagnosticsAsync(DiagnosticsQuery query, CancellationToken cancellationToken)
        => SolutionQueries.DiagnosticsAsync(Ready(), query, cancellationToken);

    public Task<SolutionMap> MapAsync(MapQuery query, CancellationToken cancellationToken)
        => SolutionQueries.MapAsync(Ready(), _solutionPath ?? string.Empty, query, cancellationToken);

    public void Dispose()
    {
        _lifetime.Cancel();
        _workspace?.Dispose();
        _lifetime.Dispose();
    }

    private Solution Ready()
    {
        var status = Status;
        return status.Phase == WorkspacePhase.Ready && _workspace is { } workspace
            ? workspace.CurrentSolution
            : throw new WorkspaceNotReadyException(status);
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
        Workspace? previous;
        try
        {
            var loaded = await loader.OpenAsync(solutionPath, progress, cancellationToken).ConfigureAwait(false);
            var solution = loaded.Workspace.CurrentSolution;
            var documents = solution.Projects.Sum(static project => project.DocumentIds.Count);
            var detail = loaded.Failures.Count == 0 ? null : $"{loaded.Failures.Count} load failures; first: {loaded.Failures[0]}";
            lock (_gate)
            {
                previous = _workspace;
                _workspace = loaded.Workspace;
                _status = new WorkspaceStatus(WorkspacePhase.Ready, solutionPath, solution.ProjectIds.Count, documents, detail);
                ReadyAt = clock.GetUtcNow();
            }
        }
#pragma warning disable CA1031 // A load failure becomes the Failed status, not a crash; every exception is caught deliberately here.
        catch (Exception error) when (error is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(error, "Opening {SolutionPath} failed.", solutionPath);
            lock (_gate)
            {
                previous = null;
                _status = new WorkspaceStatus(WorkspacePhase.Failed, solutionPath, 0, 0, error.Message);
            }
        }

        previous?.Dispose();
    }
}
