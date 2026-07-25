using System.Diagnostics;

namespace DigitalBrain.Flutter.Aspire.Hosting;

internal static class FlutterHostLaunch
{
    public const string ShellPackageDirectoryName = "shell";

    public sealed record Result(string Command, string WorkingDirectory, string[] Args);

    public static Result? Resolve(string packageRoot, FlutterHostOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(options);

        var deviceTarget = string.IsNullOrWhiteSpace(options.DeviceTarget)
            ? "windows"
            : options.DeviceTarget;
        var desktopPackage = ResolveDesktopPackageDirectory(packageRoot, deviceTarget);
        var mode = options.Mode;

        if (mode == FlutterHostMode.Auto)
        {
            mode = desktopPackage is not null && FlutterCliAvailable(options)
                ? FlutterHostMode.FlutterDesktop
                : FlutterHostMode.Headless;
        }

        if (mode == FlutterHostMode.FlutterDesktop)
        {
            var workDir = desktopPackage ?? packageRoot;
            return new Result(
                ResolveFlutterCommandForLaunch(options),
                workDir,
                ["run", "-d", deviceTarget]);
        }

        var headlessEntry = Path.Combine(
            packageRoot,
            FlutterHostingExtensions.HeadlessHostEntry.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(headlessEntry))
        {
            return null;
        }

        if (options.Mode == FlutterHostMode.Auto
            && PackageDependsOnFlutterSdk(packageRoot)
            && !FlutterCliAvailable(options))
        {
            return null;
        }

        var dart = string.IsNullOrWhiteSpace(options.DartCommand)
            ? Environment.GetEnvironmentVariable("DART_COMMAND") ?? "dart"
            : options.DartCommand;
        return new Result(dart, packageRoot, ["run", FlutterHostingExtensions.HeadlessHostEntry]);
    }

    public static string? ResolveDesktopPackageDirectory(string packageRoot, string deviceTarget)
    {
        if (HasFlutterDesktopProjectMarker(packageRoot, deviceTarget))
        {
            return packageRoot;
        }

        var shellPackage = Path.Combine(packageRoot, ShellPackageDirectoryName);
        if (HasFlutterDesktopProjectMarker(shellPackage, deviceTarget))
        {
            return shellPackage;
        }

        return null;
    }

    public static bool HasFlutterDesktopProjectMarker(string workingDirectory, string deviceTarget)
    {
        var mainDart = Path.Combine(workingDirectory, "lib", "main.dart");
        if (!File.Exists(mainDart))
        {
            return false;
        }

        var platformDir = Path.Combine(workingDirectory, deviceTarget);
        return Directory.Exists(platformDir);
    }

    public static bool PackageDependsOnFlutterSdk(string workingDirectory)
    {
        var pubspecPath = Path.Combine(workingDirectory, "pubspec.yaml");
        if (!File.Exists(pubspecPath))
        {
            return false;
        }

        foreach (var line in File.ReadLines(pubspecPath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.Contains("sdk:", StringComparison.OrdinalIgnoreCase)
                && trimmed.Contains("flutter", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool FlutterCliAvailable(FlutterHostOptions options)
    {
        foreach (var command in FlutterCommandCandidates(options))
        {
            if (TryFlutterVersion(command))
            {
                return true;
            }
        }

        return false;
    }

    public static string ResolveFlutterCommandForLaunch(FlutterHostOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.FlutterCommand))
        {
            return options.FlutterCommand;
        }

        foreach (var command in FlutterCommandCandidates(options))
        {
            if (TryFlutterVersion(command))
            {
                return command;
            }
        }

        var fromEnv = Environment.GetEnvironmentVariable("FLUTTER_COMMAND");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        return OperatingSystem.IsWindows() ? "flutter.bat" : "flutter";
    }

    private static IEnumerable<string> FlutterCommandCandidates(FlutterHostOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.FlutterCommand))
        {
            yield return options.FlutterCommand;
            yield break;
        }

        var fromEnv = Environment.GetEnvironmentVariable("FLUTTER_COMMAND");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            yield return fromEnv;
        }

        if (OperatingSystem.IsWindows())
        {
            yield return "flutter.bat";
            yield return "flutter";
        }
        else
        {
            yield return "flutter";
        }
    }

    private static bool TryFlutterVersion(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = command,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(5_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or FileNotFoundException
            or InvalidOperationException)
        {
            return false;
        }
    }
}
