using Microsoft.Extensions.Logging;

namespace DigitalBrain.UI.Aspire.Hosting;

internal sealed class FlutterHotReloadWatch : IDisposable
{
    private static readonly object Gate = new();
    private static FlutterHotReloadWatch? Active;

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Lock _reloadGate = new();
    private readonly ILogger _logger;
    private readonly int _ddsPort;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _debounce;
    private bool _reloadQueued;
    private int _disposed;

    private FlutterHotReloadWatch(int ddsPort, ILogger logger)
    {
        _ddsPort = ddsPort;
        _logger = logger;
    }

    public static void Start(
        IReadOnlyList<string> watchRoots,
        int ddsPort,
        ILogger logger,
        CancellationToken applicationStopping)
    {
        ArgumentNullException.ThrowIfNull(watchRoots);
        ArgumentNullException.ThrowIfNull(logger);

        lock (Gate)
        {
            Active?.Dispose();
            var watch = new FlutterHotReloadWatch(ddsPort, logger);
            applicationStopping.Register(watch.Dispose);
            watch.Arm(watchRoots);
            Active = watch;
            _ = watch.RunAsync();
        }
    }

    private void Arm(IReadOnlyList<string> watchRoots)
    {
        foreach (var root in watchRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var watcher = new FileSystemWatcher(root)
            {
                Filter = "*.dart",
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.CreationTime
                    | NotifyFilters.Size,
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
            _logger.LogInformation("Watching Flutter sources at {Root}", root);
        }
    }

    private async Task RunAsync()
    {
        try
        {
            await FlutterVmService
                .WaitUntilReadyAsync(_ddsPort, TimeSpan.FromMinutes(2), _lifetime.Token)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "Flutter hot reload is armed on 127.0.0.1:{Port}. Dart edits under lib/ reload the running window.",
                _ddsPort);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Flutter VM service did not become ready; file-watch hot reload is idle.");
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs args) => Queue(args.FullPath);

    private void OnChanged(object sender, FileSystemEventArgs args) => Queue(args.FullPath);

    private void Queue(string path)
    {
        if (!path.EndsWith(".dart", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}.dart_tool{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}build{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return;
        }

        lock (_reloadGate)
        {
            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var token = _debounce.Token;
            _ = DebounceAsync(token);
        }
    }

    private async Task DebounceAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), token).ConfigureAwait(false);
            await ReloadAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Flutter hot reload failed.");
        }
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        lock (_reloadGate)
        {
            if (_reloadQueued)
            {
                return;
            }

            _reloadQueued = true;
        }

        try
        {
            await FlutterVmService.ReloadAsync(_ddsPort, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Flutter hot reload applied.");
        }
        finally
        {
            lock (_reloadGate)
            {
                _reloadQueued = false;
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _lifetime.Cancel();
        _debounce?.Cancel();
        _debounce?.Dispose();
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        _lifetime.Dispose();
    }
}
