namespace DigitalBrain.Flutter.Aspire.Hosting;

internal enum FlutterHostKind
{
    Desktop = 0,
    Headless = 1,
}

internal static class FlutterHostLaunch
{
    public const string ShellPackageDirectoryName = "shell";

    public sealed record Result(string Command, string WorkingDirectory, string[] Args);

    public static Result Resolve(
        FlutterHostKind kind,
        string packageRoot,
        FlutterHostOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(options);

        return kind switch
        {
            FlutterHostKind.Desktop => ResolveDesktop(packageRoot, options),
            FlutterHostKind.Headless => ResolveHeadless(packageRoot, options),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    public static string? ResolveDesktopPackageDirectory(string packageRoot, string deviceTarget)
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

    private static Result ResolveDesktop(string packageRoot, FlutterHostOptions options)
    {
        var deviceTarget = string.IsNullOrWhiteSpace(options.DeviceTarget)
            ? "windows"
            : options.DeviceTarget;
        var workDir = ResolveDesktopPackageDirectory(packageRoot, deviceTarget)
            ?? throw new InvalidOperationException(
                $"Desktop Flutter host needs lib/main.dart and a '{deviceTarget}/' folder " +
                $"under '{packageRoot}' or '{packageRoot}/{ShellPackageDirectoryName}'. " +
                "Use WithFlutterHost<HeadlessHost>() for the pure-Dart host.");

        return new Result(
            ResolveFlutterCommand(options),
            workDir,
            ["run", "-d", deviceTarget]);
    }

    private static Result ResolveHeadless(string packageRoot, FlutterHostOptions options)
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

        var dart = string.IsNullOrWhiteSpace(options.DartCommand)
            ? Environment.GetEnvironmentVariable("DART_COMMAND") ?? "dart"
            : options.DartCommand;
        return new Result(
            dart,
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

    private static string ResolveFlutterCommand(FlutterHostOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.FlutterCommand))
        {
            return options.FlutterCommand;
        }

        var fromEnv = Environment.GetEnvironmentVariable("FLUTTER_COMMAND");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        return OperatingSystem.IsWindows() ? "flutter.bat" : "flutter";
    }
}
