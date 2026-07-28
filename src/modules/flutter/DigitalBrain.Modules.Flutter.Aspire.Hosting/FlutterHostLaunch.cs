namespace DigitalBrain.Flutter.Aspire.Hosting;

internal enum FlutterHostKind
{
    Desktop = 0,
    Headless = 1,
}

internal static class FlutterHostLaunch
{
    private const string ShellPackageDirectoryName = "shell";
    private const string ConfigurationFlutterCommandKey = "DigitalBrain:FlutterCommand";
    private const string ConfigurationDartCommandKey = "DigitalBrain:DartCommand";
    private const string DefaultFlutterCommand = "flutter";
    private const string DefaultDartCommand = "dart";

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

    /// <summary>
    /// v0.1.18 shape: options → DigitalBrain:FlutterCommand → FLUTTER_COMMAND → "flutter".
    /// When the default name is used, prefer an absolute path resolved from PATH so Aspire DCP
    /// does not depend on inheriting an interactive shell PATH (still the flutter CLI, not a .bat brand).
    /// </summary>
    internal static string ResolveFlutterCommand(
        FlutterHostOptions options,
        Microsoft.Extensions.Configuration.IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (TryConfiguredCommand(options.FlutterCommand, out var fromOptions))
        {
            return fromOptions;
        }

        if (TryConfiguredCommand(configuration?[ConfigurationFlutterCommandKey], out var fromConfig))
        {
            return fromConfig;
        }

        if (TryConfiguredCommand(
                Environment.GetEnvironmentVariable(
                    FlutterHostingExtensions.FlutterCommandEnvironmentVariable),
                out var fromEnv))
        {
            return fromEnv;
        }

        if (TryResolveCommandOnPath(DefaultFlutterCommand, out var fromPath))
        {
            return fromPath;
        }

        return DefaultFlutterCommand;
    }

    internal static string ResolveDartCommand(
        FlutterHostOptions options,
        Microsoft.Extensions.Configuration.IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (TryConfiguredCommand(options.DartCommand, out var fromOptions))
        {
            return fromOptions;
        }

        if (TryConfiguredCommand(configuration?[ConfigurationDartCommandKey], out var fromConfig))
        {
            return fromConfig;
        }

        if (TryConfiguredCommand(
                Environment.GetEnvironmentVariable(
                    FlutterHostingExtensions.DartCommandEnvironmentVariable),
                out var fromEnv))
        {
            return fromEnv;
        }

        if (TryResolveCommandOnPath(DefaultDartCommand, out var fromPath))
        {
            return fromPath;
        }

        return DefaultDartCommand;
    }

    private static bool TryConfiguredCommand(string? value, out string command)
    {
        command = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        command = value.Trim().Trim('"');
        return true;
    }

    /// <summary>
    /// Resolve a command the way a shell would: search process PATH, then User+Machine PATH,
    /// applying PATHEXT on Windows. Returns an absolute path when found.
    /// </summary>
    internal static bool TryResolveCommandOnPath(string commandName, out string absolutePath)
    {
        absolutePath = string.Empty;
        if (string.IsNullOrWhiteSpace(commandName) || Path.IsPathRooted(commandName))
        {
            if (!string.IsNullOrWhiteSpace(commandName)
                && Path.IsPathRooted(commandName)
                && File.Exists(commandName))
            {
                absolutePath = Path.GetFullPath(commandName);
                return true;
            }

            return false;
        }

        var names = CommandFileNames(commandName);
        foreach (var directory in PathSearchDirectories())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    absolutePath = Path.GetFullPath(candidate);
                    return true;
                }
            }
        }

        return false;
    }

    private static string[] CommandFileNames(string commandName)
    {
        if (!OperatingSystem.IsWindows()
            || commandName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || commandName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
            || commandName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || commandName.EndsWith(".com", StringComparison.OrdinalIgnoreCase))
        {
            return [commandName];
        }

        // PATHEXT order (common default). Prefer extensionless only if present (Unix-style shim).
        return
        [
            commandName,
            commandName + ".exe",
            commandName + ".cmd",
            commandName + ".bat",
            commandName + ".com",
        ];
    }

    private static IEnumerable<string> PathSearchDirectories()
    {
        foreach (var directory in SplitPath(Environment.GetEnvironmentVariable("PATH")))
        {
            yield return directory;
        }

        // DCP / non-interactive hosts often lack a full interactive User PATH. Merge User+Machine.
        foreach (var directory in SplitPath(
                     Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)))
        {
            yield return directory;
        }

        foreach (var directory in SplitPath(
                     Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)))
        {
            yield return directory;
        }

        var flutterRoot = Environment.GetEnvironmentVariable("FLUTTER_ROOT");
        if (!string.IsNullOrWhiteSpace(flutterRoot))
        {
            yield return Path.Combine(flutterRoot.Trim().Trim('"'), "bin");
        }
    }

    private static IEnumerable<string> SplitPath(string? path)
    {
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
