using System.Diagnostics;

namespace DigitalBrainConsole;

internal static class AspireApp
{
    internal const string DefaultAppHostProject =
        "src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj";

    public static Task StartDistributedAppAsync(
        string? appHostProject,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var root = FindRepoRoot();
        if (root is null)
        {
            return Task.CompletedTask;
        }

        var relative = string.IsNullOrWhiteSpace(appHostProject)
            ? DefaultAppHostProject
            : appHostProject;
        var appHost = Path.IsPathRooted(relative)
            ? relative
            : Path.GetFullPath(Path.Combine(root, relative));
        if (!File.Exists(appHost))
        {
            return Task.CompletedTask;
        }

        var start = new ProcessStartInfo
        {
            FileName = "aspire",
            Arguments = $"start --apphost \"{appHost}\"",
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            _ = Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
        }

        return Task.CompletedTask;
    }

    internal static string? FindRepoRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "aspire.config.json"))
                    || File.Exists(Path.Combine(dir.FullName, "DigitalBrain.slnx")))
                {
                    return dir.FullName;
                }
            }
        }

        return null;
    }
}
