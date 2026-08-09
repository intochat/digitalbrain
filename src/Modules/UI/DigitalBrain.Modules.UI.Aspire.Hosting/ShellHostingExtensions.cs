using DigitalBrain.UI;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.UI.Aspire.Hosting;

public static class ShellHostingExtensions
{
    public const string DefaultFlutterResourceName = "flutter";
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
    public const string HttpEndpointName = "http";

    public static DigitalBrainModuleBuilder<UiModule> WithHeadlessHost(
        this DigitalBrainModuleBuilder<UiModule> module,
        Action<FlutterHostOptions>? configure = null)
        => ConfigureFlutterHost(module, FlutterHostKind.Headless, configure);

    public static DigitalBrainModuleBuilder<UiModule> WithWindowHost(
        this DigitalBrainModuleBuilder<UiModule> module,
        Action<FlutterHostOptions>? configure = null)
        => ConfigureFlutterHost(module, FlutterHostKind.Window, configure);

    public static DigitalBrainModuleBuilder<UiModule> WithWebHost(
        this DigitalBrainModuleBuilder<UiModule> module,
        Action<FlutterHostOptions>? configure = null)
        => ConfigureFlutterHost(module, FlutterHostKind.Web, configure);

    private static DigitalBrainModuleBuilder<UiModule> ConfigureFlutterHost(
        DigitalBrainModuleBuilder<UiModule> module,
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

    private static ShellHostingState GetOrCreateState(DigitalBrainModuleBuilder<UiModule> module)
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
        private IResourceBuilder<ExecutableResource>? _flutterHost;
        private FlutterHostKind _flutterKind;
        private bool _uiBaseBound;

        internal void EnsureFlutterHost(FlutterHostKind kind, FlutterHostOptions options)
        {
            if (_flutterHost is not null)
            {
                throw new InvalidOperationException(
                    $"Flutter host is already configured on brain '{brain.Name}'. " +
                    $"Call {nameof(WithHeadlessHost)}, {nameof(WithWindowHost)}, or {nameof(WithWebHost)} exactly once.");
            }

            var appHost = brain.ApplicationBuilder;
            var packageRoot = ResolveFlutterWorkingDirectory(appHost.AppHostDirectory, options.WorkingDirectory);
            if (!Directory.Exists(packageRoot)
                || !File.Exists(Path.Combine(packageRoot, "pubspec.yaml")))
            {
                throw new InvalidOperationException(
                    $"Flutter host package was not found at '{packageRoot}'. " +
                    "Pass FlutterHostOptions.WorkingDirectory or place src/Modules/UI/Flutter/core in the repo.");
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

            var host = appHost
                .AddExecutable(resourceName, launch.Command, launch.WorkingDirectory, launch.Args)
                .WithEnvironment(ShellEnvironmentVariable, shell)
                .WithEnvironment(ChatEnvironmentVariable, chat);

            _flutterHost = host;
            _flutterKind = kind;
            _pendingShell = shell;
            _pendingChat = chat;
        }

        private string _pendingShell = DefaultShellName;
        private string _pendingChat = DefaultChatName;

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            if (_flutterHost is null || _uiBaseBound)
            {
                return;
            }

            var uiEndpoint = builder.GetEndpoint(HttpEndpointName);
            _flutterHost
                .WithEnvironment(UIBaseEnvironmentVariable, uiEndpoint)
                .WithAnnotation(new WaitAnnotation(builder.Resource, WaitType.WaitUntilHealthy, exitCode: 0));

            if (_flutterKind == FlutterHostKind.Web)
            {
                _flutterHost.WithArgs(
                    ReferenceExpression.Create($"--dart-define={UIBaseEnvironmentVariable}={uiEndpoint}"),
                    $"--dart-define={ShellEnvironmentVariable}={_pendingShell}",
                    $"--dart-define={ChatEnvironmentVariable}={_pendingChat}");
            }

            _uiBaseBound = true;
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
                Path.Combine(appHostDirectory, "..", "..", "Modules", "UI", "Flutter", "core"),
                Path.Combine(appHostDirectory, "..", "..", "Modules", "UI", "Flutter", "shell"),
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
