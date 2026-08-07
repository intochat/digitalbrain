using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Shell.Aspire.Hosting;

public static class ShellHostingExtensions
{
    public const string DefaultUIResourceName = "digitalbrain-ui";
    public const string DefaultFlutterResourceName = "digitalbrain-flutter";
    public const string UIBaseEnvironmentVariable = "DIGITALBRAIN_UI_BASE";
    public const string ShellEnvironmentVariable = "DIGITALBRAIN_SHELL";
    public const string ChatEnvironmentVariable = "DIGITALBRAIN_CHAT";
    public const string OwnerEnvironmentVariable = "DigitalBrain__Owner";
    public const string FlutterCommandEnvironmentVariable = "FLUTTER_COMMAND";
    public const string DartCommandEnvironmentVariable = "DART_COMMAND";
    public const string HeadlessHostEntry = "bin/digitalbrain_host.dart";
    public const string DefaultShellName = "desk";
    public const string DefaultChatName = "main";
    public const string DefaultOwner = "dev";
    public const string DefaultDeviceTarget = "windows";
    public const string DefaultWebDeviceTarget = "chrome";
    public const string WebPlatformDirectoryName = "web";
    public const string UiEdgeEndpointName = "http";
    public const string UiEdgeHealthPath = "/health";

    public static DigitalBrainModuleBuilder<ShellModule> WithUiEdge(
        this DigitalBrainModuleBuilder<ShellModule> module,
        Action<ShellUiEdgeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(module);

        var options = new ShellUiEdgeOptions();
        configure?.Invoke(options);
        GetOrCreateState(module).EnsureUiEdge(options);
        return module;
    }

    public static DigitalBrainModuleBuilder<ShellModule> WithHeadlessHost(
        this DigitalBrainModuleBuilder<ShellModule> module,
        Action<FlutterHostOptions>? configure = null)
        => ConfigureFlutterHost(module, FlutterHostKind.Headless, configure);

    public static DigitalBrainModuleBuilder<ShellModule> WithWindowHost(
        this DigitalBrainModuleBuilder<ShellModule> module,
        Action<FlutterHostOptions>? configure = null)
        => ConfigureFlutterHost(module, FlutterHostKind.Window, configure);

    public static DigitalBrainModuleBuilder<ShellModule> WithWebHost(
        this DigitalBrainModuleBuilder<ShellModule> module,
        Action<FlutterHostOptions>? configure = null)
        => ConfigureFlutterHost(module, FlutterHostKind.Web, configure);

    private static DigitalBrainModuleBuilder<ShellModule> ConfigureFlutterHost(
        DigitalBrainModuleBuilder<ShellModule> module,
        FlutterHostKind kind,
        Action<FlutterHostOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(module);

        var options = new FlutterHostOptions();
        if (kind == FlutterHostKind.Web)
        {
            options.DeviceTarget = DefaultWebDeviceTarget;
        }

        configure?.Invoke(options);
        GetOrCreateState(module).EnsureFlutterHost(kind, options);
        return module;
    }

    private static ShellHostingState GetOrCreateState(DigitalBrainModuleBuilder<ShellModule> module)
    {
        var state = module.Brain.GetOrAddState(
            static brain => new ShellHostingState(brain),
            out var added);
        if (added)
        {
            module.AddProjection(state);
        }

        return state;
    }

    private sealed class ShellHostingState(DigitalBrainBuilder brain) : DigitalBrainModuleProjection
    {
        private IResourceBuilder<ProjectResource>? _ui;
        private IResourceBuilder<ExecutableResource>? _flutterHost;

        internal void EnsureUiEdge(ShellUiEdgeOptions options)
        {
            if (_ui is not null)
            {
                throw new InvalidOperationException(
                    $"UI HTTP is already configured on brain '{brain.Name}'. Call {nameof(WithUiEdge)} exactly once.");
            }

            var appHost = brain.GetApplicationBuilder();
            var projectPath = ResolveUiEdgeProjectPath(appHost.AppHostDirectory, options.ProjectPath);
            if (!File.Exists(projectPath))
            {
                throw new InvalidOperationException(
                    $"UI HTTP project was not found at '{projectPath}'. " +
                    $"Pass {nameof(ShellUiEdgeOptions)}.{nameof(ShellUiEdgeOptions.ProjectPath)}, or place DigitalBrain.UiEdge under product/.");
            }

            var resourceName = string.IsNullOrWhiteSpace(options.ResourceName)
                ? DefaultUIResourceName
                : options.ResourceName;
            var owner = string.IsNullOrWhiteSpace(options.Owner)
                ? DefaultOwner
                : options.Owner;

            _ui = appHost
                .AddProject(resourceName, projectPath)
                .WithReference(brain.AsClient())
                .WithHttpEndpoint(
                    port: options.HttpPort,
                    name: UiEdgeEndpointName,
                    isProxied: options.HttpPort is null)
                .WithHttpHealthCheck(UiEdgeHealthPath)
                .WithEnvironment(OwnerEnvironmentVariable, owner);
        }

        internal void EnsureFlutterHost(FlutterHostKind kind, FlutterHostOptions options)
        {
            if (_flutterHost is not null)
            {
                throw new InvalidOperationException(
                    $"Flutter host is already configured on brain '{brain.Name}'. " +
                    $"Call {nameof(WithHeadlessHost)}, {nameof(WithWindowHost)}, or {nameof(WithWebHost)} exactly once.");
            }

            if (_ui is null)
            {
                EnsureUiEdge(new ShellUiEdgeOptions());
            }

            var appHost = brain.GetApplicationBuilder();
            var packageRoot = ResolveFlutterWorkingDirectory(appHost.AppHostDirectory, options.WorkingDirectory);
            if (!Directory.Exists(packageRoot)
                || !File.Exists(Path.Combine(packageRoot, "pubspec.yaml")))
            {
                throw new InvalidOperationException(
                    $"Flutter host package was not found at '{packageRoot}'. " +
                    "Pass FlutterHostOptions.WorkingDirectory or place clients/flutter/core in the repo.");
            }

            var launch = FlutterHostLaunch.Resolve(kind, packageRoot, options, appHost.Configuration);
            var resourceName = string.IsNullOrWhiteSpace(options.ResourceName)
                ? DefaultFlutterResourceName
                : options.ResourceName;
            var shell = string.IsNullOrWhiteSpace(options.ShellName)
                ? DefaultShellName
                : options.ShellName;
            var chat = string.IsNullOrWhiteSpace(options.ChatName)
                ? DefaultChatName
                : options.ChatName;
            var ui = _ui!;
            var uiEndpoint = ui.GetEndpoint(UiEdgeEndpointName);

            var host = appHost
                .AddExecutable(resourceName, launch.Command, launch.WorkingDirectory, launch.Args)
                .WithEnvironment(UIBaseEnvironmentVariable, uiEndpoint)
                .WithEnvironment(ShellEnvironmentVariable, shell)
                .WithEnvironment(ChatEnvironmentVariable, chat)
                .WaitFor(ui);

            // Browser JS cannot read process env; bake the exclusive edge contract into dart-defines.
            if (kind == FlutterHostKind.Web)
            {
                host.WithArgs(
                    ReferenceExpression.Create($"--dart-define={UIBaseEnvironmentVariable}={uiEndpoint}"),
                    $"--dart-define={ShellEnvironmentVariable}={shell}",
                    $"--dart-define={ChatEnvironmentVariable}={chat}");
            }

            _flutterHost = host;
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            if (_ui is not null)
            {
                _ui.WithAnnotation(new WaitAnnotation(builder.Resource, WaitType.WaitUntilHealthy, exitCode: 0));
            }
        }

        private static string ResolveUiEdgeProjectPath(string appHostDirectory, string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.IsPathRooted(configured)
                    ? Path.GetFullPath(configured)
                    : Path.GetFullPath(Path.Combine(appHostDirectory, configured));
            }

            string[] candidates =
            [
                Path.Combine(appHostDirectory, "..", "product", "DigitalBrain.UiEdge", "DigitalBrain.UiEdge.csproj"),
                Path.Combine(appHostDirectory, "..", "DigitalBrain.UiEdge", "DigitalBrain.UiEdge.csproj"),
            ];

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

        private static string ResolveFlutterWorkingDirectory(string appHostDirectory, string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.IsPathRooted(configured)
                    ? Path.GetFullPath(configured)
                    : Path.GetFullPath(Path.Combine(appHostDirectory, configured));
            }

            var candidates = new[]
            {
                Path.Combine(appHostDirectory, "..", "..", "clients", "flutter", "core"),
                Path.Combine(appHostDirectory, "..", "clients", "flutter", "core"),
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

