using DigitalBrain.UI;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.UI.Aspire.Hosting;

public static class ShellHostingExtensions
{
    public const string DefaultFlutterResourceName = ShellNames.DefaultFlutterResourceName;
    public const string UIBaseEnvironmentVariable = ShellNames.UIBaseEnvironmentVariable;
    public const string ShellEnvironmentVariable = ShellNames.ShellEnvironmentVariable;
    public const string ChatEnvironmentVariable = ShellNames.ChatEnvironmentVariable;
    public const string OwnerEnvironmentVariable = ShellNames.OwnerEnvironmentVariable;
    public const string FlutterCommandEnvironmentVariable = ShellNames.FlutterCommandEnvironmentVariable;
    public const string DartCommandEnvironmentVariable = ShellNames.DartCommandEnvironmentVariable;
    public const string HeadlessHostEntry = ShellNames.HeadlessHostEntry;
    public const string DefaultShellName = ShellNames.DefaultShellName;
    public const string DefaultChatName = ShellNames.DefaultChatName;
    public const string DefaultOwner = ShellNames.DefaultOwner;
    public const string DefaultDeviceTarget = ShellNames.DefaultDeviceTarget;
    public const string DefaultWebDeviceTarget = ShellNames.DefaultWebDeviceTarget;
    public const string WebPlatformDirectoryName = ShellNames.WebPlatformDirectoryName;
    public const string HttpEndpointName = ShellNames.HttpEndpointName;

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
                .WithEnvironment(ChatEnvironmentVariable, chat)
                .WithParentRelationship(brain.Resource);

            if (kind == FlutterHostKind.Web)
            {
                // FlutterHostLaunch.ResolveWeb pins --web-port/--web-hostname to these same
                // values, so this unproxied endpoint is the address flutter actually serves on.
                // The health check proves the dev server is up and answering; it cannot prove
                // the build has landed (the server answers "/" with 200 within seconds of
                // launch, before the web build completes -- observed live), so consumers that
                // need the app itself must tolerate one early page load (see UiEvidenceTests'
                // reload fallback).
                host
                    .WithHttpEndpoint(
                        port: ShellNames.FlutterWebPort,
                        name: HttpEndpointName,
                        isProxied: false)
                    .WithEndpoint(
                        HttpEndpointName,
                        static endpoint => endpoint.TargetHost = ShellNames.FlutterWebHostname,
                        createIfNotExists: false)
                    .WithHttpHealthCheck("/");
            }

            // Hot reload rides the Dart VM service, which the headless web-server target no
            // longer exposes (it runs --release; see FlutterHostLaunch.ResolveWeb). Window and
            // browser-driving web targets (e.g. "chrome" via the configure hook) keep it.
            var hasVmService = kind == FlutterHostKind.Window
                || (kind == FlutterHostKind.Web
                    && !string.Equals(
                        launch.DeviceTarget,
                        ShellNames.DefaultWebDeviceTarget,
                        StringComparison.OrdinalIgnoreCase));
            if (appHost.ExecutionContext.IsRunMode && hasVmService)
            {
                ArmHotReload(host, launch.WorkingDirectory);
            }

            _flutterHost = host;
            _flutterKind = kind;
            _pendingShell = shell;
            _pendingChat = chat;
        }

        private static void ArmHotReload(IResourceBuilder<ExecutableResource> host, string workingDirectory)
        {
            var ddsPort = ShellNames.FlutterDdsPort;
            var watchRoots = ResolveWatchRoots(workingDirectory);
            host
                .WithArgs(
                    $"--dds-port={ddsPort}",
                    $"--host-vmservice-port={ShellNames.FlutterVmServicePort}",
                    "--disable-service-auth-codes")
                .WithUrl("http://127.0.0.1:" + ddsPort + "/devtools", "DevTools")
                .WithCommand(
                    "hot-reload",
                    "Hot Reload",
                    async context =>
                    {
                        try
                        {
                            await FlutterVmService.ReloadAsync(ddsPort, context.CancellationToken)
                                .ConfigureAwait(false);
                            return CommandResults.Success();
                        }
                        catch (Exception ex)
                        {
                            return CommandResults.Failure(ex.Message);
                        }
                    },
                    new CommandOptions
                    {
                        IconName = "ArrowSync",
                        UpdateState = static context =>
                            string.Equals(context.ResourceSnapshot.State?.Text, "Running", StringComparison.Ordinal)
                                ? ResourceCommandState.Enabled
                                : ResourceCommandState.Disabled,
                    });

            host.OnResourceReady((resource, @event, _) =>
            {
                var logger = @event.Services
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("DigitalBrain.FlutterHotReload");
                var lifetime = @event.Services.GetRequiredService<IHostApplicationLifetime>();
                FlutterHotReloadWatch.Start(watchRoots, ddsPort, logger, lifetime.ApplicationStopping);
                return Task.CompletedTask;
            });
        }

        private static string[] ResolveWatchRoots(string workingDirectory)
        {
            var family = Path.GetFullPath(Path.Combine(workingDirectory, ".."));
            return
            [
                Path.Combine(workingDirectory, "lib"),
                Path.Combine(family, "kit", "lib"),
                Path.Combine(family, "core", "lib"),
            ];
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
            // Flutter executable waits until the kernel HTTP surface is healthy.
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
