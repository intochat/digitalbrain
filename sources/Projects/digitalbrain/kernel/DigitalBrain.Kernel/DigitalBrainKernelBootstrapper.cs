using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Introspector;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Runtime.Ui;
using DigitalBrain.Runtime.User;
using DigitalBrain.Kernel.Cortex;
using DigitalBrain.Kernel.Creator;
using DigitalBrain.Kernel.Creator.InoAuthoring;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.Navigator;
using DigitalBrain.Kernel.Runtime;
using DigitalBrain.Kernel.Visualization;
using DigitalBrain.InoLang.Linking;
using DigitalBrain.SDK.Google;
using DigitalBrain.SDK.Sqlite;
using DigitalBrain.SDK.DigitalBrain.SoftwareEngineering;
using DigitalBrain.SDK.Stripe;
using DigitalBrain.Runtime.Runtime.Settings;
using DigitalBrain.Kernel.Runtime.Settings;
using DigitalBrain.Runtime.Dynamic;
using DigitalBrain.SDK.DigitalBrain.Ai;
using DigitalBrain.SDK.DigitalBrain.Ai.GrokUiDesigner;
using DigitalBrain.SDK.DigitalBrain.Ai.NemoChat;
using DigitalBrain.SDK.DigitalBrain.Ui;
using DigitalBrain.SDK.DigitalBrain.Onboarding;
using DigitalBrain.SDK.Microsoft.Windows.Runtime;
using DigitalBrain.SDK.DigitalBrain.Identity;

namespace DigitalBrain.Kernel;

public static class DigitalBrainKernelBootstrapper
{
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        // Force load domain and SDK assemblies so Orleans and NeuronCatalogScanner register compiled grains
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet();
        foreach (var file in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if ((name.StartsWith("DigitalBrain.Domains.", StringComparison.OrdinalIgnoreCase) || 
                 name.StartsWith("DigitalBrain.", StringComparison.OrdinalIgnoreCase)) && 
                !loadedAssemblies.Contains(name))
            {
                try
                {
                    System.Reflection.Assembly.LoadFrom(file);
                }
                catch { }
            }
        }

        var configuration = builder.Configuration;

        // Configure Kestrel ports
        var aspnetUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        if (!string.IsNullOrEmpty(aspnetUrls))
        {
            builder.WebHost.ConfigureKestrel(options =>
            {
                foreach (var urlStr in aspnetUrls.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Uri.TryCreate(urlStr, UriKind.Absolute, out var uri))
                    {
                        if (uri.Scheme == Uri.UriSchemeHttp)
                        {
                            options.ListenLocalhost(uri.Port, listenOptions =>
                            {
                                listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
                            });
                        }
                        else if (uri.Scheme == Uri.UriSchemeHttps)
                        {
                            options.ListenLocalhost(uri.Port, listenOptions =>
                            {
                                listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
                                listenOptions.UseHttps();
                            });
                        }
                    }
                }
            });
        }

        builder.AddDigitalBrainDomain();
        builder.AddDigitalBrainSiloDomains();
        builder.Services.AddDigitalBrainOtlpForwardClient();
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<IInoPackageLoader, InoPackageLoader>();

        builder.Services.AddSingleton(_ =>
        {
            var touch = new[]
            {
                typeof(LlmRequest),
                typeof(SqliteExecRequest),
                typeof(GmailDigestReady),
                typeof(PlanNeuronRequest),
                typeof(RfwCard),
                typeof(UserPromptReceived),
                typeof(NemoChatRequest),
                typeof(GrokUiDesignRequest),
                typeof(GrokUiDesignResponse),
                typeof(SaveUiToInoRequest),
                typeof(SaveUiToInoResponse),
                typeof(CreateTask),
                typeof(ExplainDecisionRequest),
                typeof(DeveloperSandboxReport),
                typeof(CreateFolderRequest),
                typeof(RequestOnboarding),
                typeof(OnboardingResult),
                typeof(AcceptPolicy),
                typeof(PolicyAccepted),
                typeof(RequestSetting),
                typeof(RequestCreateBrain),
                typeof(DigitalBrain.Runtime.Marketplace.GetBundlesQuery),
                typeof(RequestDigestFeed),
                typeof(LayoutRequest),
                typeof(NavigateRequest),
                typeof(PositionRequest),
                typeof(VisualStateRequest),
                typeof(ResourceFailed),
                typeof(RestartResource),
                typeof(HealTopographyRequest),
                typeof(HealTopographyResponse),
            };
            GC.KeepAlive(touch);

            var reg = new SynapsePayloadRegistry();
            reg.RegisterDiscoveredSynapses();
            return reg;
        });

        builder.Services.AddSingleton(TimeProvider.System);

        // Linker substrate (E-RUN #34): catalog for the production InoCompiler, plus
        // the silo-local plan cache that consumes it (E-RUN #33).
        builder.Services.AddSingleton<IContractCatalog>(_ =>
            new AssemblyScanningContractCatalog(AssemblyScanningContractCatalog.DiscoverContractAssemblies()));
        builder.Services.AddSingleton<InoDefinitionCache>();
        builder.Services.AddSingleton<IPlanCache, InMemoryPlanCache>();

        // E-RUN #44. Startup invariant — every loaded neuron-target grain's [GrainType]
        // must resolve to a ContractKind.Neuron entry in the catalog. Symmetric to
        // the catalog's own neuron scan (#41); throws at StartAsync if the two drift,
        // so the silo refuses gateway traffic until the .ino → grain surface is
        // consistent.
        builder.Services.AddHostedService<NeuronCatalogInvariantHostedService>();

        builder.Services.AddSingleton<IDynamicMirrorPath, DynamicMirrorPath>();
        builder.Services.AddSingleton<IGeneratedNeuronStore, GeneratedNeuronStore>();
        builder.Services.AddSingleton<GherkinValidator>();
        builder.Services.AddSingleton<StepCompileStage>();
        builder.Services.AddSingleton<ImplCompileStage>();
        builder.Services.AddSingleton<NavigatorRouter>();
        builder.Services.AddSingleton<SynapseBroadcaster>();
        
        // E-SDK #60. ISynapseEmitter is the kernel-provided emit facade L3 SDK
        // connectors consume via DI — keeps the connector decoupled from
        // SynapseBroadcaster and the gateway. The adapter delegates to the
        // broadcaster's port-less system-signal path.
        builder.Services.AddSingleton<ISynapseEmitter, SynapseEmitter>();
        
        // E-SDK #63. Production lifecycle registry — IInterpretedNeuronSource
        // implementations register via DI; the registry aggregates them at silo
        // start, publishes catalog entries, and serves as the lookup the grain's
        // OnActivateAsync consults to lazy-auto-configure.
        builder.Services.AddSingleton<InterpretedNeuronRegistry>();
        builder.Services.AddSingleton<IInterpretedNeuronRegistry>(sp => sp.GetRequiredService<InterpretedNeuronRegistry>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<InterpretedNeuronRegistry>());
        builder.Services.AddSingleton<DigitalBrain.Runtime.Runtime.IBundleInstaller, Runtime.LocalBundleInstaller>();
        builder.Services.AddTransient<IStartupTask, OS.KernelOSBootstrapper>();

        // E-SDK #57. Creator-authored InoLang persistence — the loop drives
        // red→green and writes .ino + manifest under the Generated root.
        // Live authoring registers the green result immediately; this source
        // rehydrates those definitions at a later silo start.
        builder.Services.AddSingleton<IInoGeneratedRoot, InoGeneratedRoot>();
        builder.Services.AddSingleton<IInoNeuronStore, InoNeuronStore>();
        builder.Services.AddSingleton<InoAuthoringLoop>();
        builder.Services.AddSingleton<IInterpretedNeuronSource, DynamicGeneratedInoSource>();
        builder.Services.AddSingleton<DynamicDomainRegistry>();
        builder.Services.AddSingleton<IInterpretedNeuronSource, SqliteDynamicNeuronSource>();
        builder.Services.AddSingleton<IInterpretedNeuronSource, KernelInoSource>();
        builder.Services.AddSingleton(new PredicateNeuronBinding("is-valid-token", SettingsStoreGrain.GrainTypeId));
        builder.Services.AddSingleton(new PredicateNeuronBinding("is-d-drive-prompt", "DigitalBrain.Ai.SlmNeuron"));
        builder.Services.AddSingleton(new PredicateNeuronBinding("is-microsoft-create-prompt", "DigitalBrain.Ai.SlmNeuron"));
        builder.Services.AddSingleton(new PredicateNeuronBinding("is-flutter-web", "DigitalBrain.SDK.Aspire.Runtime.IsFlutterWebPredicate"));
        builder.Services.AddSingleton<GatewayCorrelationTracker>();
        builder.Services.AddSingleton<HomeFeedBus>();
        builder.Services.AddSingleton<IAiHealthProbe, AiHealthProbe>();
        builder.Services.AddSingleton<NeuronFeatureLoader>();
        builder.Services.AddSingleton<INeuronFeatureLoader>(sp => sp.GetRequiredService<NeuronFeatureLoader>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<NeuronFeatureLoader>());
        builder.Services.AddHostedService<TimelineRelayActivator>();
        builder.Services.AddHostedService<IntentDispatcherActivator>();
        builder.Services.Configure<TaskManagerOptions>(_ => { });
        builder.Services.AddSingleton<ITaskManagerBroadcaster, HomeFeedBroadcaster>();
        builder.Services.AddHostedService<TaskManagerTicker>();
        builder.Services.Configure<FlutterPerfOptions>(configuration.GetSection("DigitalBrain:Visualization:FlutterPerf"));
        builder.Services.AddSingleton<IFlutterPerfBroadcaster, FlutterPerfBroadcaster>();
        builder.Services.AddSingleton<IFlutterPerfHintBroadcaster, FlutterPerfHintBroadcaster>();
        builder.Services.AddHostedService<FlutterPerfTicker>();
        builder.Services.AddHostedService<DigitalBrain.Kernel.Runtime.Watchers.InoFilesystemWatcher>();


        // E-RUN #38: v3 §L7 kernel-owned system hooks. Singleton because the
        // gateway calls EmitBrainStartedIfFirstAsync on shell-connect, and the
        // once-per-kernel flag has to survive across gateway requests.
        builder.Services.AddSingleton<SystemHookEmitter>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SystemHookEmitter>());

        builder.Services.AddDigitalBrainSdkWindows();
        builder.Services.AddStripeConnector();
        builder.Services.AddGrpc();

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("flutter-web", policy => policy
                .SetIsOriginAllowed(origin =>
                    Uri.TryCreate(origin, UriKind.Absolute, out var u)
                    && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
                    && (u.Host == "localhost"
                        || u.Host == "127.0.0.1"
                        || u.Host == "::1"
                        || u.Host == "[::1]"
                        || u.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)))
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithExposedHeaders(
                    "grpc-status", "grpc-message", "grpc-status-details-bin",
                    "grpc-encoding", "grpc-accept-encoding"));
        });
    }

    public static void ConfigurePipeline(WebApplication app)
    {
        app.UseRouting();
        app.UseCors("flutter-web");
        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
        app.MapDefaultEndpoints();
        app.MapDigitalBrainOtlpProxy();
        app.MapGrpcService<DigitalBrainGatewayService>().EnableGrpcWeb().RequireCors("flutter-web");
        app.MapGrpcService<BrainWatchService>().EnableGrpcWeb().RequireCors("flutter-web");
        app.MapGrpcService<BrainRegistryService>().EnableGrpcWeb().RequireCors("flutter-web");
        app.MapGrpcService<UiGatewayService>().EnableGrpcWeb().RequireCors("flutter-web");
    }
}
