using System.Diagnostics;
using Aspire.Hosting.Testing;

namespace DigitalBrain.Testing;

public static class HostedApplication
{
    public const string CollectionName = "Hosted";

    public static readonly string[] DefaultTrackedProcessNames =
    [
        "DigitalBrain.ProbeHost",
        "DigitalBrain.Host",
    ];

    private static readonly SemaphoreSlim ExclusiveGate = new(1, 1);
    private static string? s_exclusiveOwner;

    public static bool IsExclusiveHeld => ExclusiveGate.CurrentCount == 0;

    public static string? ExclusiveOwner => s_exclusiveOwner;

    public static async Task<IAsyncDisposable> HoldExclusiveAsync(
        string ownerLabel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerLabel);

        await ExclusiveGate.WaitAsync(cancellationToken);
        s_exclusiveOwner = ownerLabel;
        return new ExclusiveLease(ownerLabel);
    }

    public static Task<IDistributedApplicationTestingBuilder> CreateBuilderAsync<TEntryPoint>(
        CancellationToken cancellationToken = default)
        where TEntryPoint : class
        => DistributedApplicationTestingBuilder.CreateAsync<TEntryPoint>(cancellationToken);

    public static async Task<HostedScenario> OpenAsync<TEntryPoint>(
        TimeSpan? startupTimeout = null,
        IReadOnlyList<string>? trackedProcessNames = null,
        CancellationToken cancellationToken = default)
        where TEntryPoint : class
    {
        var timeout = startupTimeout ?? TimeSpan.FromMinutes(5);
        var processNames = trackedProcessNames ?? DefaultTrackedProcessNames;
        var ownerLabel = typeof(TEntryPoint).FullName ?? typeof(TEntryPoint).Name;
        var lease = await HoldExclusiveAsync(ownerLabel, cancellationToken);
        var baselineProcessIds = SnapshotProcessIds(processNames);

        try
        {
            var builder = await DistributedApplicationTestingBuilder.CreateAsync<TEntryPoint>(
                args: [],
                configureBuilder: static (options, _) => options.EnableResourceLogging = true,
                cancellationToken);
            var app = await builder.BuildAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
            await app.StartAsync(cancellationToken).WaitAsync(timeout, cancellationToken);

            return new HostedScenario(
                app,
                lease,
                timeout,
                processNames,
                baselineProcessIds);
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }
    }

    internal static HashSet<int> SnapshotProcessIds(IReadOnlyList<string> processNames)
    {
        var ids = new HashSet<int>();
        foreach (var name in processNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            foreach (var process in processes)
            {
                try
                {
                    ids.Add(process.Id);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return ids;
    }

    private sealed class ExclusiveLease(string ownerLabel) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            if (string.Equals(s_exclusiveOwner, ownerLabel, StringComparison.Ordinal))
            {
                s_exclusiveOwner = null;
            }

            ExclusiveGate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
