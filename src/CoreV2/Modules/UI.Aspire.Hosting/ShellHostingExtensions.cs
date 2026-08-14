using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Brain.Modules.UI;
using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Brain.Modules.UI.Aspire.Hosting;

public static class ShellHostingExtensions
{
    public const string ProductBaseEnvironmentVariable = ShellNames.ProductBaseEnvironmentVariable;

    public static DigitalBrainModuleBuilder<UiModule> WithWindowHost(
        this DigitalBrainModuleBuilder<UiModule> module,
        Action<FlutterHostOptions>? configure = null)
        => ConfigureFlutterHost(module, FlutterHostKind.Window, configure);

    public static DigitalBrainModuleBuilder<UiModule> WithWebHost(
        this DigitalBrainModuleBuilder<UiModule> module,
        Action<FlutterHostOptions>? configure = null)
        => ConfigureFlutterHost(module, FlutterHostKind.Web, configure);

    public static DigitalBrainModuleBuilder<UiModule> WithHeadlessHost(
        this DigitalBrainModuleBuilder<UiModule> module,
        Action<FlutterHostOptions>? configure = null)
        => ConfigureFlutterHost(module, FlutterHostKind.Headless, configure);

    private static DigitalBrainModuleBuilder<UiModule> ConfigureFlutterHost(
        DigitalBrainModuleBuilder<UiModule> module,
        FlutterHostKind kind,
        Action<FlutterHostOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(module);

        var options = new FlutterHostOptions();
        if (kind == FlutterHostKind.Web)
        {
            options.DeviceTarget = ShellNames.DefaultWebDeviceTarget;
        }

        configure?.Invoke(options);
        var state = module.Brain.GetOrAddState(
            static brain => new ShellHostingState(brain),
            out var added);
        if (added)
        {
            module.AddProjection(state);
        }

        state.EnsureFlutterHost(kind, options);
        return module;
    }

    private sealed class ShellHostingState(DigitalBrainBuilder brain) : DigitalBrainModuleProjection
    {
        private IResourceBuilder<ExecutableResource>? _flutter;
        private FlutterHostKind _kind;
        private string _shell = ShellNames.DefaultShellName;
        private bool _productBound;
        private FlutterHotReloadWatch? _hotReloadWatch;

        internal void EnsureFlutterHost(FlutterHostKind kind, FlutterHostOptions options)
        {
            if (_flutter is not null)
            {
                throw new InvalidOperationException(
                    $"Flutter host is already configured on brain '{brain.Name}'. Configure exactly one window, web, or headless host.");
            }

            var packageRoot = ResolveWorkingDirectory(brain.ApplicationBuilder.AppHostDirectory, options.WorkingDirectory);
            if (!File.Exists(Path.Combine(packageRoot, "pubspec.yaml")))
            {
                throw new InvalidOperationException(
                    $"Flutter package was not found at '{packageRoot}'. Set {nameof(FlutterHostOptions.WorkingDirectory)} or create the CoreV2 shell.");
            }

            var launch = FlutterHostLaunch.Resolve(kind, packageRoot, options, brain.ApplicationBuilder.Configuration);
            var resourceName = string.IsNullOrWhiteSpace(options.ResourceName)
                ? ShellNames.DefaultFlutterResourceName
                : options.ResourceName;
            _shell = string.IsNullOrWhiteSpace(options.ShellName)
                ? ShellNames.DefaultShellName
                : options.ShellName;
            _flutter = brain.ApplicationBuilder
                .AddExecutable(resourceName, launch.Command, launch.WorkingDirectory, launch.Args)
                .WithEnvironment(ShellNames.ShellEnvironmentVariable, _shell);
            _kind = kind;

            if (brain.ApplicationBuilder.ExecutionContext.IsRunMode
                && kind is FlutterHostKind.Window or FlutterHostKind.Web)
            {
                ArmHotReload(
                    _flutter,
                    launch.Command,
                    launch.WorkingDirectory,
                    launch.Args[2]);
            }
        }

        public override void ApplyToClient<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (_flutter is null || _productBound)
            {
                return;
            }

            var productEndpoint = builder.GetEndpoint(ShellNames.HttpEndpointName);
            _flutter
                .WithEnvironment(ShellNames.ProductBaseEnvironmentVariable, productEndpoint)
                .WithAnnotation(new WaitAnnotation(builder.Resource, WaitType.WaitUntilHealthy, exitCode: 0));

            if (_kind == FlutterHostKind.Web)
            {
                _flutter.WithArgs(
                    ReferenceExpression.Create(
                        $"--dart-define={ShellNames.ProductBaseEnvironmentVariable}={productEndpoint}"),
                    $"--dart-define={ShellNames.ShellEnvironmentVariable}={_shell}");
            }

            _productBound = true;
        }

        private void ArmHotReload(
            IResourceBuilder<ExecutableResource> flutter,
            string flutterCommand,
            string workingDirectory,
            string deviceTarget)
        {
            flutter
                .WithArgs(
                    $"--dds-port={ShellNames.FlutterDdsPort}",
                    $"--host-vmservice-port={ShellNames.FlutterVmServicePort}",
                    "--disable-service-auth-codes")
                .WithUrl(
                    "http://127.0.0.1:54721/devtools",
                    "Flutter DevTools")
                .WithCommand(
                    "hot-reload",
                    "Hot Reload",
                    async context =>
                    {
                        try
                        {
                            await FlutterHotReloadRunner.ReloadAsync(
                                flutterCommand,
                                workingDirectory,
                                deviceTarget,
                                ShellNames.FlutterDdsPort,
                                context.CancellationToken).ConfigureAwait(false);
                            return CommandResults.Success();
                        }
                        catch (Exception exception)
                        {
                            return CommandResults.Failure(exception.Message);
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

            flutter.OnResourceReady((_, ready, _) =>
            {
                var logger = ready.Services
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("DigitalBrain.CoreV2.Flutter");
                var lifetime = ready.Services.GetRequiredService<IHostApplicationLifetime>();
                _hotReloadWatch?.Dispose();
                _hotReloadWatch = FlutterHotReloadWatch.Start(
                    ResolveWatchRoots(workingDirectory),
                    ShellNames.FlutterDdsPort,
                    flutterCommand,
                    workingDirectory,
                    deviceTarget,
                    logger,
                    lifetime.ApplicationStopping);
                return Task.CompletedTask;
            });
        }

        private static string[] ResolveWatchRoots(string workingDirectory)
            =>
            [
                Path.Combine(workingDirectory, "lib"),
                Path.GetFullPath(Path.Combine(workingDirectory, "..", "core", "lib")),
            ];

        private static string ResolveWorkingDirectory(string appHostDirectory, string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.IsPathRooted(configured)
                    ? Path.GetFullPath(configured)
                    : Path.GetFullPath(Path.Combine(appHostDirectory, configured));
            }

            return Path.GetFullPath(
                Path.Combine(appHostDirectory, "..", "UI", "Flutter", "shell"));
        }
    }
}
