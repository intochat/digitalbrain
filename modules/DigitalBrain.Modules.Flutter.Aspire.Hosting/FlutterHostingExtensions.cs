using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Flutter.Aspire.Hosting;

public static class FlutterHostingExtensions
{
    public const string DefaultUiResourceName = "digitalbrain-ui";
    public const string DefaultFlutterResourceName = "digitalbrain-flutter";
    public const string UiBaseEnvironmentVariable = "DIGITALBRAIN_UI_BASE";
    public const string OwnerEnvironmentVariable = "DigitalBrain__Owner";

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
            var command = string.IsNullOrWhiteSpace(options.FlutterCommand)
                ? Environment.GetEnvironmentVariable("FLUTTER_COMMAND") ?? "flutter"
                : options.FlutterCommand;
            var target = string.IsNullOrWhiteSpace(options.DeviceTarget)
                ? "windows"
                : options.DeviceTarget;

            var ui = _ui!;
            var endpoint = ui.GetEndpoint("http");
            _flutterHost = appHost
                .AddExecutable(resourceName, command, workingDirectory, "run", "-d", target)
                .WithEnvironment(UiBaseEnvironmentVariable, endpoint)
                .WithReference(endpoint)
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

public sealed class FlutterUiEdgeOptions
{
    public string ResourceName { get; set; } = FlutterHostingExtensions.DefaultUiResourceName;

    public string Owner { get; set; } = "dev";

    public string? ProjectPath { get; set; }
}

public sealed class FlutterHostOptions
{
    public string ResourceName { get; set; } = FlutterHostingExtensions.DefaultFlutterResourceName;

    public string DeviceTarget { get; set; } = "windows";

    public string? FlutterCommand { get; set; }

    public string? WorkingDirectory { get; set; }

    public bool RequireHost { get; set; }
}
