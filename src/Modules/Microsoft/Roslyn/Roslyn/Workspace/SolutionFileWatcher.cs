using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Microsoft.Roslyn;

// Saves from the owner's editor, dotnet format or a git checkout reach the snapshot without a reload;
// project-file changes only flag that a reload is needed (design 4.2).
public sealed class SolutionFileWatcher(SolutionWorkspace workspace, TimeProvider clock, ILogger<SolutionFileWatcher> logger) : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(200);
    private static readonly string[] IgnoredSegments = ["/bin/", "/obj/", "/.git/", "/artifacts/", "/node_modules/"];
    private readonly ConcurrentDictionary<string, DateTimeOffset> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _loop;
    private string? _directory;
    private bool _disposed;

    public static bool IsIgnored(string path)
    {
        var normalized = path.Replace('\\', '/');
        return IgnoredSegments.Any(segment => normalized.Contains(segment, StringComparison.OrdinalIgnoreCase));
    }

    public void Start(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        lock (_gate)
        {
            if (_disposed || string.Equals(_directory, directory, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(directory))
            {
                return;
            }

            StopCore();
            _directory = directory;
            _watcher = new FileSystemWatcher(directory) { IncludeSubdirectories = true, InternalBufferSize = 64 * 1024, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
            _watcher.Changed += (_, args) => Enqueue(args.FullPath);
            _watcher.Created += (_, args) => Enqueue(args.FullPath);
            _watcher.Renamed += (_, args) =>
            {
                // Both ends of a rename matter: the new path may be a document to fold, and the old one has
                // just disappeared from the snapshot, which only a reload can reconcile.
                Enqueue(args.OldFullPath);
                Enqueue(args.FullPath);
            };
            _watcher.Error += (_, args) =>
            {
                var error = args.GetException();
                logger.LogWarning(error, "The file watcher for {Directory} reported an error.", directory);
                workspace.MarkReloadNeeded("watcher: " + error.Message);
            };
            _watcher.EnableRaisingEvents = true;
            _loop = new CancellationTokenSource();
            _ = DrainAsync(_loop.Token);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopCore();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            StopCore();
        }
    }

    private void Enqueue(string path)
    {
        if (IsIgnored(path))
        {
            return;
        }

        var extension = Path.GetExtension(path);
        if (extension is ".cs" or ".csproj" or ".props" or ".targets" or ".slnx" or ".sln")
        {
            _pending[path] = clock.GetUtcNow();
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Settle);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (_pending.IsEmpty)
                {
                    continue;
                }

                foreach (var (path, seen) in _pending.ToArray())
                {
                    // A conditional remove: if a newer save re-stamped this path after the snapshot above,
                    // the entry stays put for the next tick instead of being dropped underneath it.
                    if (clock.GetUtcNow() - seen < Settle || !_pending.TryRemove(new KeyValuePair<string, DateTimeOffset>(path, seen)))
                    {
                        continue;
                    }

                    try
                    {
                        await ApplyAsync(path, cancellationToken).ConfigureAwait(false);
                    }
#pragma warning disable CA1031 // the watcher must outlive any single bad path
                    catch (Exception error) when (error is not OperationCanceledException)
#pragma warning restore CA1031
                    {
                        logger.LogWarning(error, "Could not process {Path}; the watcher continues.", path);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ApplyAsync(string path, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            workspace.MarkReloadNeeded(path);
            return;
        }

        if (!File.Exists(path))
        {
            // A deleted or renamed-away document cannot be folded: the snapshot still has it, so only a
            // reload can reconcile the file list.
            workspace.MarkReloadNeeded(path);
            return;
        }

        try
        {
            var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (!await workspace.FoldAsync(path.Replace('\\', '/'), text, cancellationToken).ConfigureAwait(false))
            {
                // Unchanged text (our own commit's write coming back) or a path the snapshot does not carry
                // as a document; either way the next save re-enqueues it, so this is a trace, not a warning.
                logger.LogDebug("{Path} was not folded into the snapshot.", path);
            }
        }
        catch (IOException error)
        {
            // The editor still holds the file; the next save re-enqueues it.
            logger.LogDebug(error, "Could not read {Path} after a save; waiting for the next one.", path);
        }
        catch (InvalidOperationException error)
        {
            logger.LogDebug(error, "Could not fold {Path}.", path);
        }
    }

    private void StopCore()
    {
        _loop?.Cancel();
        _loop?.Dispose();
        _loop = null;
        _watcher?.Dispose();
        _watcher = null;
        _directory = null;
        _pending.Clear();
    }
}