using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Flutter.Aspire.Hosting;

public enum FlutterHostMode
{
    Auto = 0,
    FlutterDesktop = 1,
    Headless = 2,
}

public static class FlutterHostingExtensions
{
    public const string DefaultUiResourceName = "digitalbrain-ui";
    public const string DefaultFlutterResourceName = "digitalbrain-flutter";
    public const string UiBaseEnvironmentVariable = "DIGITALBRAIN_UI_BASE";
    public const string ShellEnvironmentVariable = "DIGITALBRAIN_SHELL";
    public const string OwnerEnvironmentVariable = "DigitalBrain__Owner";
    public const string HeadlessHostEntry = "bin/digitalbrain_host.dart";
    public const string DefaultShellName = "desk";

    public static DigitalBrainModuleBuilder<FlutterModule> WithUiEdge(
        this DigitalBrainModuleBuilder<FlutterModule> module,
        Action<FlutterUiEdgeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(module);

        var options = new FlutterUiEdgeOptions();
        configure?.Invoke(options);
        GetOrCreateState(module).EnsureUiEdge(options);
        return module;
    }

    public static DigitalBrainModuleBuilder<FlutterModule> WithFlutterHost(
        this DigitalBrainModuleBuilder<FlutterModule> module,
        Action<FlutterHostOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(module);

        var options = new FlutterHostOptions();
        configure?.Invoke(options);
        GetOrCreateState(module).EnsureFlutterHost(options);
        return module;
    }

    private static FlutterHostingState GetOrCreateState(
        DigitalBrainModuleBuilder<FlutterModule> module)
    {
        var state = module.Brain.GetOrAddState(
            static brain => new FlutterHostingState(brain),
            out var added);
        if (added)
        {
            module.AddProjection(state);
        }

        return state;
    }

    private sealed class FlutterHostingState(DigitalBrainBuilder brain) : DigitalBrainModuleProjection
    {
        private IResourceBuilder<ProjectResource>? _ui;
        private IResourceBuilder<ExecutableResource>? _flutterHost;

        internal void EnsureUiEdge(FlutterUiEdgeOptions options)
        {
            if (_ui is not null)
            {
                throw new InvalidOperationException(
                    $"Ui edge is already configured on brain '{brain.Name}'. Call WithUiEdge exactly once.");
            }

            var appHost = brain.GetApplicationBuilder();
            var projectPath = ResolveUiProjectPath(appHost.AppHostDirectory, options.ProjectPath);
            if (!File.Exists(projectPath))
            {
                throw new InvalidOperationException(
                    $"Flutter Ui edge project was not found at '{projectPath}'. " +
                    "Pass FlutterUiEdgeOptions.ProjectPath or place DigitalBrain.Ui next to the AppHost under hosts/.");
            }

            var resourceName = string.IsNullOrWhiteSpace(options.ResourceName)
                ? DefaultUiResourceName
                : options.ResourceName;

            var owner = string.IsNullOrWhiteSpace(options.Owner)
                ? "dev"
                : options.Owner;

            _ui = appHost
                .AddProject(resourceName, projectPath)
                .WithReference(brain.AsClient())
                .WithEnvironment(OwnerEnvironmentVariable, owner);
        }

        internal void EnsureFlutterHost(FlutterHostOptions options)
        {
            if (_flutterHost is not null)
            {
                throw new InvalidOperationException(
                    $"Flutter host is already configured on brain '{brain.Name}'. Call WithFlutterHost exactly once.");
            }

            if (_ui is null)
            {
                EnsureUiEdge(new FlutterUiEdgeOptions());
            }

            var appHost = brain.GetApplicationBuilder();
            var workingDirectory = ResolveFlutterWorkingDirectory(
                appHost.AppHostDirectory,
                options.WorkingDirectory);
            if (!Directory.Exists(workingDirectory)
                || !File.Exists(Path.Combine(workingDirectory, "pubspec.yaml")))
            {
                if (options.RequireHost)
                {
                    throw new InvalidOperationException(
                        $"Flutter host package was not found at '{workingDirectory}'. " +
                        "Pass FlutterHostOptions.WorkingDirectory or place clients/digitalbrain_flutter in the repo.");
                }

                return;
            }

            var resourceName = string.IsNullOrWhiteSpace(options.ResourceName)
                ? DefaultFlutterResourceName
                : options.ResourceName;
            var shell = string.IsNullOrWhiteSpace(options.ShellName)
                ? DefaultShellName
                : options.ShellName;
            var ui = _ui!;
            var endpoint = ui.GetEndpoint("http");

            var launch = ResolveHostLaunch(workingDirectory, options);
            if (launch is null)
            {
                if (options.RequireHost)
                {
                    throw new InvalidOperationException(
                        "Flutter host could not be launched: Flutter CLI missing and headless entry not found. " +
                        "Install Flutter, pass FlutterHostMode.Headless with bin/digitalbrain_host.dart, or set RequireHost false.");
                }

                return;
            }

            _flutterHost = appHost
                .AddExecutable(resourceName, launch.Command, workingDirectory, launch.Args)
                .WithEnvironment(UiBaseEnvironmentVariable, endpoint)
                .WithEnvironment(ShellEnvironmentVariable, shell)
                .WithReference(endpoint)
                .WaitFor(ui);
        }

        private static HostLaunch? ResolveHostLaunch(
            string workingDirectory,
            FlutterHostOptions options)
        {
            var mode = options.Mode;
            if (mode == FlutterHostMode.Auto)
            {
                mode = FlutterCliAvailable(options)
                    ? FlutterHostMode.FlutterDesktop
                    : FlutterHostMode.Headless;
            }

            if (mode == FlutterHostMode.FlutterDesktop)
            {
                var command = string.IsNullOrWhiteSpace(options.FlutterCommand)
                    ? Environment.GetEnvironmentVariable("FLUTTER_COMMAND") ?? "flutter"
                    : options.FlutterCommand;
                var target = string.IsNullOrWhiteSpace(options.DeviceTarget)
                    ? "windows"
                    : options.DeviceTarget;
                return new HostLaunch(command, ["run", "-d", target]);
            }

            var headlessEntry = Path.Combine(workingDirectory, HeadlessHostEntry.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(headlessEntry))
            {
                return null;
            }

            var dart = string.IsNullOrWhiteSpace(options.DartCommand)
                ? Environment.GetEnvironmentVariable("DART_COMMAND") ?? "dart"
                : options.DartCommand;
            return new HostLaunch(dart, ["run", HeadlessHostEntry]);
        }

        private static bool FlutterCliAvailable(FlutterHostOptions options)
        {
            var command = string.IsNullOrWhiteSpace(options.FlutterCommand)
                ? Environment.GetEnvironmentVariable("FLUTTER_COMMAND") ?? "flutter"
                : options.FlutterCommand;

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

        private sealed record HostLaunch(string Command, string[] Args);

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            if (_ui is not null)
            {
                _ui.WithAnnotation(new WaitAnnotation(
                    builder.Resource,
                    WaitType.WaitUntilHealthy,
                    exitCode: 0));
            }
        }

        private static string ResolveUiProjectPath(string appHostDirectory, string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.IsPathRooted(configured)
                    ? Path.GetFullPath(configured)
                    : Path.GetFullPath(Path.Combine(appHostDirectory, configured));
            }

            var candidates = new[]
            {
                Path.Combine(appHostDirectory, "..", "DigitalBrain.Ui", "DigitalBrain.Ui.csproj"),
                Path.Combine(appHostDirectory, "..", "..", "hosts", "DigitalBrain.Ui", "DigitalBrain.Ui.csproj"),
            };

            foreach (var candidate in candidates)
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                {
                    return full;
                }
            }

            return Path.GetFullPath(candidates[0]);
        }

        private static string ResolveFlutterWorkingDirectory(
            string appHostDirectory,
            string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.IsPathRooted(configured)
                    ? Path.GetFullPath(configured)
                    : Path.GetFullPath(Path.Combine(appHostDirectory, configured));
            }

            var candidates = new[]
            {
                Path.Combine(appHostDirectory, "..", "..", "clients", "digitalbrain_flutter"),
                Path.Combine(appHostDirectory, "..", "clients", "digitalbrain_flutter"),
            };

            foreach (var candidate in candidates)
            {
                var full = Path.GetFullPath(candidate);
                if (Directory.Exists(full) && File.Exists(Path.Combine(full, "pubspec.yaml")))
                {
                    return full;
                }
            }

            return Path.GetFullPath(candidates[0]);
        }
    }
}

public sealed class FlutterUiEdgeOptions
{
    public string ResourceName { get; set; } = FlutterHostingExtensions.DefaultUiResourceName;

    public string Owner { get; set; } = "dev";

    public string? ProjectPath { get; set; }
}

public sealed class FlutterHostOptions
{
    public string ResourceName { get; set; } = FlutterHostingExtensions.DefaultFlutterResourceName;

    public FlutterHostMode Mode { get; set; } = FlutterHostMode.Auto;

    public string DeviceTarget { get; set; } = "windows";

    public string ShellName { get; set; } = FlutterHostingExtensions.DefaultShellName;

    public string? FlutterCommand { get; set; }

    public string? DartCommand { get; set; }

    public string? WorkingDirectory { get; set; }

    public bool RequireHost { get; set; }
}
