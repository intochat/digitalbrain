using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Hosting.Tray;

// Single entry point for digitalbrain.cs / DigitalBrain.AppHost: opt the
// AppHost process into the tray daemon when `--tray` (or `--profile=product`)
// is on the launch command line. Keeps the v5 launch file a one-liner.
public static class TrayHostingExtensions
{
    public static IDistributedApplicationBuilder AddTrayDaemonIfRequested(
        this IDistributedApplicationBuilder builder,
        string[] args)
    {
        if (!ShouldStartTray(args)) return builder;
        builder.Services.AddHostedService<TrayDaemon>();
        return builder;
    }

    // Called at the very top of the launch file. If args contain `--url`,
    // hand the URL to the already-running daemon over the named pipe and
    // signal the caller to exit. Returns true when the caller should exit.
    public static bool TryForwardUrlAndExit(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--url", StringComparison.OrdinalIgnoreCase))
                return SingleInstancePipe.TryForward(args[i + 1]);
        }
        return false;
    }

    private static bool ShouldStartTray(string[] args)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase))
                return true;
            if (arg.StartsWith("--profile=", StringComparison.OrdinalIgnoreCase) &&
                arg.AsSpan("--profile=".Length).Equals("product", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
