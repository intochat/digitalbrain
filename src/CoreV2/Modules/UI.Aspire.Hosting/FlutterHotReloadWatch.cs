using Microsoft.Extensions.Logging;

namespace Brain.Modules.UI.Aspire.Hosting;

internal sealed class FlutterHotReloadWatch : IDisposable
{
    private readonly int _port;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private CancellationTokenSource? _debounce;
    private int _disposed;

    private FlutterHotReloadWatch(int port, ILogger logger)
    {
        _port = port;
        _logger = logger;
    }

    internal static FlutterHotReloadWatch Start(
        IEnumerable<string> roots,
        int port,
        ILogger logger,
        CancellationToken stopping)
    {
        var watch = new FlutterHotReloadWatch(port, logger);
        stopping.Register(watch.Dispose);
        watch.Arm(roots);
        return watch;
    }

    private void Arm(IEnumerable<string> roots)
    {
        foreach (var root in roots.Where(Directory.Exists))
        {
            var watcher = new FileSystemWatcher(root, "*.dart")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            watcher.Changed += OnChange;
            watcher.Created += OnChange;
            watcher.Renamed += OnChange;
            _watchers.Add(watcher);
        }
    }

    private void OnChange(object sender, FileSystemEventArgs args)
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _ = ReloadAfterDebounceAsync(_debounce.Token);
    }

    private async Task ReloadAfterDebounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken).ConfigureAwait(false);
            await FlutterVmService.ReloadAsync(_port, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Flutter hot reload applied.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Flutter hot reload failed.");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        _debounce?.Cancel();
        _debounce?.Dispose();
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _lifetime.Dispose();
    }
}
