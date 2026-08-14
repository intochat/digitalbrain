using Microsoft.Extensions.Configuration;

namespace Brain.Modules.UI.Aspire.Hosting;

internal static class FlutterHostLaunch
{
    private const string ConfigurationFlutterCommandKey = "DigitalBrain:FlutterCommand";
    private const string ConfigurationDartCommandKey = "DigitalBrain:DartCommand";

    internal sealed record Result(string Command, string WorkingDirectory, string[] Args);

    internal static Result Resolve(
        FlutterHostKind kind,
        string packageRoot,
        FlutterHostOptions options,
        IConfiguration? configuration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(options);

        return kind switch
        {
            FlutterHostKind.Window => ResolveWindow(packageRoot, options, configuration),
            FlutterHostKind.Headless => ResolveHeadless(packageRoot, options, configuration),
            FlutterHostKind.Web => ResolveWeb(packageRoot, options, configuration),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static Result ResolveWindow(
        string packageRoot,
        FlutterHostOptions options,
        IConfiguration? configuration)
    {
        var deviceTarget = string.IsNullOrWhiteSpace(options.DeviceTarget)
            ? ShellNames.DefaultDeviceTarget
            : options.DeviceTarget;
        RequireFile(packageRoot, Path.Combine("lib", "main.dart"), "Window Flutter host");
        RequireDirectory(packageRoot, deviceTarget, "Window Flutter host");
        return new Result(
            ResolveCommand(options.FlutterCommand, configuration?[ConfigurationFlutterCommandKey], ShellNames.FlutterCommandEnvironmentVariable, "flutter"),
            packageRoot,
            ["run", "-d", deviceTarget]);
    }

    private static Result ResolveWeb(
        string packageRoot,
        FlutterHostOptions options,
        IConfiguration? configuration)
    {
        var deviceTarget = string.IsNullOrWhiteSpace(options.DeviceTarget)
            || string.Equals(options.DeviceTarget, ShellNames.DefaultDeviceTarget, StringComparison.OrdinalIgnoreCase)
            ? ShellNames.DefaultWebDeviceTarget
            : options.DeviceTarget;
        RequireFile(packageRoot, Path.Combine("lib", "main.dart"), "Web Flutter host");
        RequireFile(packageRoot, Path.Combine(ShellNames.WebPlatformDirectoryName, "index.html"), "Web Flutter host");
        return new Result(
            ResolveCommand(options.FlutterCommand, configuration?[ConfigurationFlutterCommandKey], ShellNames.FlutterCommandEnvironmentVariable, "flutter"),
            packageRoot,
            ["run", "-d", deviceTarget]);
    }

    private static Result ResolveHeadless(
        string packageRoot,
        FlutterHostOptions options,
        IConfiguration? configuration)
    {
        RequireFile(packageRoot, ShellNames.HeadlessHostEntry.Replace('/', Path.DirectorySeparatorChar), "Headless Flutter host");
        return new Result(
            ResolveCommand(options.DartCommand, configuration?[ConfigurationDartCommandKey], ShellNames.DartCommandEnvironmentVariable, "dart"),
            packageRoot,
            ["run", ShellNames.HeadlessHostEntry]);
    }

    private static string ResolveCommand(string? option, string? configuration, string environmentKey, string fallback)
    {
        foreach (var value in new[] { option, configuration, Environment.GetEnvironmentVariable(environmentKey) })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim().Trim('"');
            }
        }

        return TryResolveCommandOnPath(fallback, out var command) ? command : fallback;
    }

    private static bool TryResolveCommandOnPath(string command, out string absolutePath)
    {
        var extensions = OperatingSystem.IsWindows()
            ? new[] { string.Empty, ".exe", ".cmd", ".bat", ".com" }
            : [string.Empty];
        var pathValues = new[]
        {
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
        };

        foreach (var pathValue in pathValues)
        {
            if (string.IsNullOrWhiteSpace(pathValue))
            {
                continue;
            }

            foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory.Trim().Trim('"'), command + extension);
                    if (File.Exists(candidate))
                    {
                        absolutePath = Path.GetFullPath(candidate);
                        return true;
                    }
                }
            }
        }

        absolutePath = string.Empty;
        return false;
    }

    private static void RequireFile(string root, string relativePath, string host)
    {
        if (!File.Exists(Path.Combine(root, relativePath)))
        {
            throw new InvalidOperationException($"{host} needs '{relativePath}' under '{root}'.");
        }
    }

    private static void RequireDirectory(string root, string relativePath, string host)
    {
        if (!Directory.Exists(Path.Combine(root, relativePath)))
        {
            throw new InvalidOperationException($"{host} needs a '{relativePath}/' directory under '{root}'.");
        }
    }
}
