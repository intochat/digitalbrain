using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Flutter.Aspire.Hosting;

public static class FlutterHostingExtensions
{
    public const string DefaultUiResourceName = "digitalbrain-ui";
    public const string DefaultFlutterResourceName = "digitalbrain-flutter";
    public const string UiBaseEnvironmentVariable = "DIGITALBRAIN_UI_BASE";
    public const string ShellEnvironmentVariable = "DIGITALBRAIN_SHELL";
    public const string OwnerEnvironmentVariable = "DigitalBrain__Owner";
    public const string FlutterCommandEnvironmentVariable = "FLUTTER_COMMAND";
    public const string DartCommandEnvironmentVariable = "DART_COMMAND";
    public const string HeadlessHostEntry = "bin/digitalbrain_host.dart";
    public const string DefaultShellName = "desk";
    public const string DefaultOwner = "dev";
    public const string DefaultDeviceTarget = "windows";
    public const string UiHttpEndpointName = "http";
    public const string UiHealthPath = "/health";

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
        => WithFlutterHost<DesktopHost>(module, configure);

    public static DigitalBrainModuleBuilder<FlutterModule> WithFlutterHost<THost>(
        this DigitalBrainModuleBuilder<FlutterModule> module,
        Action<FlutterHostOptions>? configure = null)
        where THost : class
    {
        ArgumentNullException.ThrowIfNull(module);

        var options = new FlutterHostOptions();
        configure?.Invoke(options);
        GetOrCreateState(module).EnsureFlutterHost(HostKindOf<THost>(), options);
        return module;
    }

    private static FlutterHostKind HostKindOf<THost>()
        where THost : class
    {
        if (typeof(THost) == typeof(DesktopHost))
        {
            return FlutterHostKind.Desktop;
        }

        if (typeof(THost) == typeof(HeadlessHost))
        {
            return FlutterHostKind.Headless;
        }

        throw new NotSupportedException(
            $"{typeof(THost).FullName} is not a Flutter host kind. " +
            $"Use {nameof(DesktopHost)} or {nameof(HeadlessHost)}.");
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
                ? DefaultOwner
                : options.Owner;

            _ui = appHost
                .AddProject(resourceName, projectPath)
                .WithReference(brain.AsClient())
                .WithHttpEndpoint(name: UiHttpEndpointName)
                .WithHttpHealthCheck(UiHealthPath)
                .WithEnvironment(OwnerEnvironmentVariable, owner);
        }

        internal void EnsureFlutterHost(FlutterHostKind kind, FlutterHostOptions options)
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
            var packageRoot = ResolveFlutterWorkingDirectory(
                appHost.AppHostDirectory,
                options.WorkingDirectory);
            if (!Directory.Exists(packageRoot)
                || !File.Exists(Path.Combine(packageRoot, "pubspec.yaml")))
            {
                throw new InvalidOperationException(
                    $"Flutter host package was not found at '{packageRoot}'. " +
                    "Pass FlutterHostOptions.WorkingDirectory or place clients/digitalbrain_flutter in the repo.");
            }

            var launch = FlutterHostLaunch.Resolve(
                kind,
                packageRoot,
                options,
                appHost.Configuration);
            var resourceName = string.IsNullOrWhiteSpace(options.ResourceName)
                ? DefaultFlutterResourceName
                : options.ResourceName;
            var shell = string.IsNullOrWhiteSpace(options.ShellName)
                ? DefaultShellName
                : options.ShellName;
            var ui = _ui!;

            _flutterHost = appHost
                .AddExecutable(resourceName, launch.Command, launch.WorkingDirectory, launch.Args)
                .WithEnvironment(UiBaseEnvironmentVariable, ui.GetEndpoint(UiHttpEndpointName))
                .WithEnvironment(ShellEnvironmentVariable, shell)
                .WaitFor(ui);
        }

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

