namespace DigitalBrain.Flutter.Aspire.Hosting;

internal enum FlutterHostKind
{
    Desktop = 0,
    Headless = 1,
}

internal static class FlutterHostLaunch
{
    private const string ShellPackageDirectoryName = "shell";
    private const string FlutterRootEnvironmentVariable = "FLUTTER_ROOT";
    private const string ConfigurationFlutterCommandKey = "DigitalBrain:FlutterCommand";
    private const string ConfigurationDartCommandKey = "DigitalBrain:DartCommand";

    internal sealed record Result(string Command, string WorkingDirectory, string[] Args);

    internal static Result Resolve(
        FlutterHostKind kind,
        string packageRoot,
        FlutterHostOptions options,
        Microsoft.Extensions.Configuration.IConfiguration? configuration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(options);

        return kind switch
        {
            FlutterHostKind.Desktop => ResolveDesktop(packageRoot, options, configuration),
            FlutterHostKind.Headless => ResolveHeadless(packageRoot, options, configuration),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static string? ResolveDesktopPackageDirectory(string packageRoot, string deviceTarget)
    {
        if (HasDesktopMarkers(packageRoot, deviceTarget))
        {
            return packageRoot;
        }

        var shellPackage = Path.Combine(packageRoot, ShellPackageDirectoryName);
        if (HasDesktopMarkers(shellPackage, deviceTarget))
        {
            return shellPackage;
        }

        return null;
    }

    private static Result ResolveDesktop(
        string packageRoot,
        FlutterHostOptions options,
        Microsoft.Extensions.Configuration.IConfiguration? configuration)
    {
        var deviceTarget = string.IsNullOrWhiteSpace(options.DeviceTarget)
            ? FlutterHostingExtensions.DefaultDeviceTarget
            : options.DeviceTarget;
        var workDir = ResolveDesktopPackageDirectory(packageRoot, deviceTarget)
            ?? throw new InvalidOperationException(
                $"Desktop Flutter host needs lib/main.dart and a '{deviceTarget}/' folder " +
                $"under '{packageRoot}' or '{packageRoot}/{ShellPackageDirectoryName}'. " +
                "Use WithFlutterHost<HeadlessHost>() for the pure-Dart host.");

        return new Result(
            ResolveFlutterCommand(options, configuration),
            workDir,
            ["run", "-d", deviceTarget]);
    }

    private static Result ResolveHeadless(
        string packageRoot,
        FlutterHostOptions options,
        Microsoft.Extensions.Configuration.IConfiguration? configuration)
    {
        var entry = Path.Combine(
            packageRoot,
            FlutterHostingExtensions.HeadlessHostEntry.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(entry))
        {
            throw new InvalidOperationException(
                $"Headless Flutter host needs '{FlutterHostingExtensions.HeadlessHostEntry}' under '{packageRoot}'. " +
                "Use WithFlutterHost() / WithFlutterHost<DesktopHost>() for Windows chrome under shell/.");
        }

        return new Result(
            ResolveDartCommand(options, configuration),
            packageRoot,
            ["run", FlutterHostingExtensions.HeadlessHostEntry]);
    }

    private static bool HasDesktopMarkers(string workingDirectory, string deviceTarget)
    {
        var mainDart = Path.Combine(workingDirectory, "lib", "main.dart");
        if (!File.Exists(mainDart))
        {
            return false;
        }

        return Directory.Exists(Path.Combine(workingDirectory, deviceTarget));
    }

    internal static string ResolveFlutterCommand(
        FlutterHostOptions options,
        Microsoft.Extensions.Configuration.IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (TryExplicitCommand(options.FlutterCommand, out var fromOptions))
        {
            return fromOptions;
        }

        var fromConfiguration = configuration?[ConfigurationFlutterCommandKey];
        if (TryExplicitCommand(fromConfiguration, out var configured))
        {
            return configured;
        }

        var fromEnv = Environment.GetEnvironmentVariable(
            FlutterHostingExtensions.FlutterCommandEnvironmentVariable);
        if (TryExplicitCommand(fromEnv, out var envCommand))
        {
            return envCommand;
        }

        if (TryFindFlutterCli(out var discovered))
        {
            return discovered;
        }

        throw new InvalidOperationException(
            "Flutter CLI was not found for WithFlutterHost(). " +
            "Set DigitalBrain:FlutterCommand in AppHost configuration, " +
            $"{FlutterHostingExtensions.FlutterCommandEnvironmentVariable}, " +
            $"{FlutterRootEnvironmentVariable}, or install Flutter on PATH. " +
            "DCP does not inherit an interactive shell PATH; prefer an absolute path " +
            $"(e.g. E:\\tools\\flutter\\bin\\flutter.bat). Tried PATH entries and " +
            $"{FlutterRootEnvironmentVariable}/bin.");
    }

    internal static string ResolveDartCommand(
        FlutterHostOptions options,
        Microsoft.Extensions.Configuration.IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (TryExplicitCommand(options.DartCommand, out var fromOptions))
        {
            return fromOptions;
        }

        var fromConfiguration = configuration?[ConfigurationDartCommandKey];
        if (TryExplicitCommand(fromConfiguration, out var configured))
        {
            return configured;
        }

        var fromEnv = Environment.GetEnvironmentVariable(
            FlutterHostingExtensions.DartCommandEnvironmentVariable);
        if (TryExplicitCommand(fromEnv, out var envCommand))
        {
            return envCommand;
        }

        if (TryFindDartCli(out var discovered))
        {
            return discovered;
        }

        return OperatingSystem.IsWindows() ? "dart.bat" : "dart";
    }

    private static bool TryExplicitCommand(string? value, out string command)
    {
        command = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim().Trim('"');
        if (Path.IsPathRooted(trimmed))
        {
            if (!File.Exists(trimmed))
            {
                throw new InvalidOperationException(
                    $"Configured Flutter/Dart command '{trimmed}' does not exist.");
            }

            command = Path.GetFullPath(trimmed);
            return true;
        }

        command = trimmed;
        return true;
    }

    private static bool TryFindFlutterCli(out string path)
    {
        foreach (var candidate in FlutterExecutableCandidates())
        {
            if (File.Exists(candidate))
            {
                path = Path.GetFullPath(candidate);
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    private static bool TryFindDartCli(out string path)
    {
        foreach (var candidate in DartExecutableCandidates())
        {
            if (File.Exists(candidate))
            {
                path = Path.GetFullPath(candidate);
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    private static IEnumerable<string> FlutterExecutableCandidates()
    {
        var fileNames = OperatingSystem.IsWindows()
            ? new[] { "flutter.bat", "flutter.cmd", "flutter" }
            : new[] { "flutter" };

        foreach (var root in SdkRoots())
        {
            foreach (var fileName in fileNames)
            {
                yield return Path.Combine(root, "bin", fileName);
            }
        }

        foreach (var directory in PathDirectories())
        {
            foreach (var fileName in fileNames)
            {
                yield return Path.Combine(directory, fileName);
            }
        }
    }

    private static IEnumerable<string> DartExecutableCandidates()
    {
        var fileNames = OperatingSystem.IsWindows()
            ? new[] { "dart.bat", "dart.cmd", "dart" }
            : new[] { "dart" };

        foreach (var root in SdkRoots())
        {
            foreach (var fileName in fileNames)
            {
                yield return Path.Combine(root, "bin", fileName);
            }
        }

        foreach (var directory in PathDirectories())
        {
            foreach (var fileName in fileNames)
            {
                yield return Path.Combine(directory, fileName);
            }
        }
    }

    private static IEnumerable<string> SdkRoots()
    {
        var flutterRoot = Environment.GetEnvironmentVariable(FlutterRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(flutterRoot))
        {
            yield return flutterRoot.Trim().Trim('"');
        }

        // Well-known Windows dev layouts used by this monorepo (not user-profile paths).
        if (OperatingSystem.IsWindows())
        {
            yield return @"E:\tools\flutter";
            yield return @"E:\flutter";
            yield return @"C:\src\flutter";
            yield return @"C:\flutter";
        }
    }

    private static IEnumerable<string> PathDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = segment.Trim().Trim('"');
            if (directory.Length > 0)
            {
                yield return directory;
            }
        }
    }
}
