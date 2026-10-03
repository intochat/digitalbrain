using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Flutter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Flutter.Aspire.Hosting;

public static class ShellHostingExtensions
{
    public static DigitalBrainModuleBuilder<DigitalBrain.Flutter.Aspire.Hosting.FlutterModuleHosting> RunDesktopApp(
        this DigitalBrainModuleBuilder<DigitalBrain.Flutter.Aspire.Hosting.FlutterModuleHosting> module,
        Action<FlutterHostOptions>? configure = null)
        => ConfigureFlutterHost(module, FlutterHostKind.Window, configure);

    public static DigitalBrainModuleBuilder<DigitalBrain.Flutter.Aspire.Hosting.FlutterModuleHosting> RunWebApp(
        this DigitalBrainModuleBuilder<DigitalBrain.Flutter.Aspire.Hosting.FlutterModuleHosting> module,
        Action<FlutterHostOptions>? configure = null)
        => ConfigureFlutterHost(module, FlutterHostKind.Web, configure);

    private static DigitalBrainModuleBuilder<DigitalBrain.Flutter.Aspire.Hosting.FlutterModuleHosting> ConfigureFlutterHost(
        DigitalBrainModuleBuilder<DigitalBrain.Flutter.Aspire.Hosting.FlutterModuleHosting> module,
        FlutterHostKind kind,
        Action<FlutterHostOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(module);

        var options = new FlutterHostOptions();
        if (kind == FlutterHostKind.Web)
        {
            options.DeviceTarget = ShellNames.DefaultWebDeviceTarget;
        }

        module.DigitalBrainBuilder.ApplicationBuilder.Configuration.GetSection(FlutterHostOptions.SectionName).Bind(options);
        options.Kind = kind;
        configure?.Invoke(options);
        GetOrCreateState(module).EnsureFlutterHost(kind, options);
        return module;
    }

    private static ShellHostingState GetOrCreateState(DigitalBrainModuleBuilder<DigitalBrain.Flutter.Aspire.Hosting.FlutterModuleHosting> module)
    {
        var state = module.DigitalBrainBuilder.GetOrAddState(
            static brain => new ShellHostingState(brain),
            out var added);
        if (added)
        {
            module.AddProjection(state);
        }

        return state;
    }

    internal static string ResolveFlutterWorkingDirectory(string appHostDirectory, string? configured)
        => ShellHostingState.ResolveFlutterWorkingDirectory(appHostDirectory, configured);

    private sealed class ShellHostingState(DigitalBrainBuilder brain) : DigitalBrainModuleProjection
    {
        private IResourceBuilder<ExecutableResource>? _flutterHost;
        private IResourceBuilder<ContainerResource>? _shellContainer;
        private string? _servedConfigPath;
        private FlutterHostKind _flutterKind;
        private bool _uiBaseBound;

        internal void EnsureFlutterHost(FlutterHostKind kind, FlutterHostOptions options)
        {
            if (_flutterHost is not null || _shellContainer is not null)
            {
                throw new InvalidOperationException(
                    $"Flutter host is already configured on brain '{brain.Name}'. " +
                    $"Call {nameof(RunDesktopApp)} or {nameof(RunWebApp)} exactly once.");
            }

            var appHost = brain.ApplicationBuilder;
            var packageRoot = ResolveFlutterWorkingDirectory(appHost.AppHostDirectory, options.WorkingDirectory);
            if (!Directory.Exists(packageRoot)
                || !File.Exists(Path.Combine(packageRoot, "pubspec.yaml")))
            {
                throw new InvalidOperationException(
                    $"Flutter host package was not found at '{packageRoot}'. " +
                    "Pass FlutterHostOptions.WorkingDirectory or place src/Modules/Google/Flutter/app/core in the repo.");
            }

            var resourceName = string.IsNullOrWhiteSpace(options.ResourceName)
                ? brain.ResourceName(ShellNames.DefaultFlutterResourceName)
                : options.ResourceName;
            var shell = string.IsNullOrWhiteSpace(options.ShellName)
                ? ShellNames.DefaultShellName
                : options.ShellName;
            var chat = string.IsNullOrWhiteSpace(options.ChatName)
                ? ShellNames.DefaultChatName
                : options.ChatName;
            _flutterKind = kind;
            _pendingShell = shell;
            _pendingChat = chat;

            if (kind == FlutterHostKind.Web && options.WebContainer)
            {
                EnsureShellContainer(appHost, packageRoot, resourceName);
                return;
            }

            var launch = FlutterHostLaunch.Resolve(kind, packageRoot, options, appHost.Configuration);

            var host = appHost
                .AddExecutable(resourceName, launch.Command, launch.WorkingDirectory, launch.Args)
                .WithEnvironment(ShellNames.ShellEnvironmentVariable, shell)
                .WithEnvironment(ShellNames.ChatEnvironmentVariable, chat)
                .WithParentRelationship(brain.Resource);

            if (kind == FlutterHostKind.Web)
            {
                if (string.Equals(launch.DeviceTarget, ShellNames.DefaultWebDeviceTarget, StringComparison.OrdinalIgnoreCase))
                {
                    var buildCheck = resourceName + "-build";
                    appHost.Services.AddHealthChecks().Add(new HealthCheckRegistration(buildCheck,
                        services => new FlutterWebBuildHealthCheck(services.GetRequiredService<ResourceLoggerService>(), host.Resource,
                            host.GetEndpoint(ShellNames.HttpEndpointName)),
                        failureStatus: null, tags: null));
                    host.WithHealthCheck(buildCheck);
                }
                // HTTP verifies the server; the separate build check rejects old cached
                // assets until this process reports its current compilation is served.
                host
                    .WithHttpEndpoint(
                        name: ShellNames.HttpEndpointName,
                        env: "DIGITALBRAIN_WEB_PORT",
                        isProxied: false)
                    .WithEndpoint(
                        ShellNames.HttpEndpointName,
                        static endpoint => endpoint.TargetHost = ShellNames.FlutterWebHostname,
                        createIfNotExists: false)
                    .WithHttpHealthCheck("/")
                    .AsBrainBrowser(path: "/?semantics=true", readySelector: "flt-semantics");
                host.WithArgs(ReferenceExpression.Create($"--web-port={host.GetEndpoint(ShellNames.HttpEndpointName).Property(EndpointProperty.TargetPort)}"));
            }

            // Hot reload rides the Dart VM service, which the headless web-server target no
            // longer exposes (it runs --release; see FlutterHostLaunch.ResolveWeb). Window and
            // browser-driving web targets (e.g. "chrome" via the configure hook) keep it.
            var hasVmService = !options.ReleaseBuild
                && (kind == FlutterHostKind.Window
                    || (kind == FlutterHostKind.Web
                        && !string.Equals(
                            launch.DeviceTarget,
                            ShellNames.DefaultWebDeviceTarget,
                            StringComparison.OrdinalIgnoreCase)));
            if (appHost.ExecutionContext.IsRunMode && hasVmService)
            {
                ArmHotReload(host, launch.WorkingDirectory);
            }

            _flutterHost = host;
        }

        // Compile once per source change, serve per session: the image bakes the release
        // bundle (docker layer cache is the staleness logic), and the kernel URL arrives at
        // serve time through the config.json this AppHost writes before the container starts.
        private void EnsureShellContainer(
            IDistributedApplicationBuilder appHost,
            string packageRoot,
            string resourceName)
        {
            var shellDirectory = FlutterHostLaunch.ResolveWebPackageDirectory(packageRoot)
                ?? throw new InvalidOperationException(
                    $"Containerized web shell needs lib/main.dart and a 'web/' folder under " +
                    $"'{packageRoot}' or its 'shell' sibling.");
            var dockerfile = Path.Combine(shellDirectory, "Dockerfile");
            if (!File.Exists(dockerfile))
            {
                throw new InvalidOperationException(
                    $"Containerized web shell needs a Dockerfile at '{dockerfile}'.");
            }

            // The pub workspace root: the shell's core and ui siblings belong to the build context.
            var workspaceRoot = Path.GetFullPath(Path.Combine(shellDirectory, ".."));

            // The bind-mount source must exist when docker creates the container; the real
            // content lands in Apply right before start, once the kernel endpoint is allocated.
            _servedConfigPath = Path.Combine(
                Directory.CreateTempSubdirectory("digitalbrain-shell-").FullName, "config.json");
            File.WriteAllText(_servedConfigPath, "{}");

            _shellContainer = appHost
                .AddDockerfile(resourceName, workspaceRoot, Path.GetRelativePath(workspaceRoot, dockerfile))
                .WithHttpEndpoint(targetPort: 80, name: ShellNames.HttpEndpointName)
                .WithHttpHealthCheck("/")
                .WithBindMount(_servedConfigPath, ShellServedConfig.ContainerPath, isReadOnly: true)
                .WithParentRelationship(brain.Resource)
                .AsBrainBrowser(path: "/?semantics=true", readySelector: "flt-semantics");
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
            // A Dart workspace can include application packages outside the shell's
            // module. Watch its sources too, including on the first pub resolution.
            for (var directory = new DirectoryInfo(workingDirectory); directory is not null; directory = directory.Parent)
            {
                var manifest = Path.Combine(directory.FullName, "pubspec.yaml");
                if (File.Exists(manifest) && File.ReadLines(manifest).Any(line => line.TrimEnd() == "workspace:"))
                { return [directory.FullName]; }
            }
            var family = Path.GetFullPath(Path.Combine(workingDirectory, ".."));
            return
            [
                Path.Combine(workingDirectory, "lib"),
                Path.Combine(family, "ui", "lib"),
                Path.Combine(family, "core", "lib"),
            ];
        }

        private string _pendingShell = ShellNames.DefaultShellName;
        private string _pendingChat = ShellNames.DefaultChatName;

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            if (_uiBaseBound)
            {
                return;
            }

            if (_shellContainer is not null)
            {
                var kernelEndpoint = builder.GetEndpoint(ShellNames.HttpEndpointName);
                var configPath = _servedConfigPath!;
                var shellName = _pendingShell;
                var chatName = _pendingChat;
                _shellContainer
                    .WithAnnotation(new WaitAnnotation(builder.Resource, WaitType.WaitUntilHealthy, exitCode: 0))
                    // Endpoints are allocated before any resource starts, so the kernel URL is real here.
                    .OnBeforeResourceStarted((_, _, ct) => File.WriteAllTextAsync(
                        configPath, ShellServedConfig.Payload(kernelEndpoint.Url, shellName, chatName), ct));
                builder.WithEnvironment(
                    EnvironmentKeys.For("DigitalBrain:Cors", "AllowedOrigin"),
                    _shellContainer.GetEndpoint(ShellNames.HttpEndpointName));
                _uiBaseBound = true;
                return;
            }

            if (_flutterHost is null)
            {
                return;
            }

            var uiEndpoint = builder.GetEndpoint(ShellNames.HttpEndpointName);
            // Flutter executable waits until the kernel HTTP surface is healthy.
            _flutterHost
                .WithEnvironment(ShellNames.UIBaseEnvironmentVariable, uiEndpoint)
                .WithAnnotation(new WaitAnnotation(builder.Resource, WaitType.WaitUntilHealthy, exitCode: 0));

            if (_flutterKind == FlutterHostKind.Web)
            {
                builder.WithEnvironment(
                    EnvironmentKeys.For("DigitalBrain:Cors", "AllowedOrigin"),
                    _flutterHost.GetEndpoint(ShellNames.HttpEndpointName));
                _flutterHost.WithArgs(
                    ReferenceExpression.Create($"--dart-define={ShellNames.UIBaseEnvironmentVariable}={uiEndpoint}"),
                    $"--dart-define={ShellNames.ShellEnvironmentVariable}={_pendingShell}",
                    $"--dart-define={ShellNames.ChatEnvironmentVariable}={_pendingChat}");
            }

            _uiBaseBound = true;
        }

        internal static string ResolveFlutterWorkingDirectory(string appHostDirectory, string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.IsPathRooted(configured)
                    ? Path.GetFullPath(configured)
                    : Path.GetFullPath(Path.Combine(appHostDirectory, configured));
            }

            // Walk up rather than guessing a fixed depth: the caller can be an application AppHost
            // or any test project, and those sit at different depths under the repository root.
            string[] packagePathsFromRepositoryRoot =
            [
                Path.Combine("src", "Modules", "Google", "Flutter", "app", "core"),
                Path.Combine("src", "Modules", "Google", "Flutter", "app", "shell"),
            ];
            for (var directory = new DirectoryInfo(Path.GetFullPath(appHostDirectory)); directory is not null; directory = directory.Parent)
            {
                foreach (var packagePath in packagePathsFromRepositoryRoot)
                {
                    var candidate = Path.Combine(directory.FullName, packagePath);
                    if (File.Exists(Path.Combine(candidate, "pubspec.yaml"))) { return candidate; }
                }
            }

            throw new DirectoryNotFoundException(
                $"No Flutter package above '{appHostDirectory}'. Expected {string.Join(" or ", packagePathsFromRepositoryRoot)} "
                + "under the repository root, or pass FlutterHostOptions.WorkingDirectory.");
        }
    }
}
