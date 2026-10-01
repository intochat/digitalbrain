using DigitalBrain.AI;
using DigitalBrain.Flutter;
using IntoChat.Tests.E2E.Agent;
using IntoChat.Tests.E2E.Diagnostics;

namespace IntoChat.Tests.E2E;

// The one product host of the assembly, booted on the first fact that leases it: the standard
// composition plus the web shell, a scripted model endpoint, an OTLP collector, and the shipped
// apps published through the real gate. Facts isolate through their lease's WorkspaceId and
// fact-owned neuron ids; only a fact that needs a different composition boots its own host.
public sealed class IntoChatHostFixture : SharedBrainFixture, IAsyncLifetime
{
    // xUnit initializes the assembly fixture before any fact runs, so the boot (web shell
    // compile, shipped-app verification) never counts against a fact's timeout.
    public async ValueTask InitializeAsync() => await WarmUpAsync();

    public static readonly string[] ShippedPackages =
        ["intochat/assistant", "intochat/customer-researcher", "intochat/group-chat", "intochat/settings", "intochat/word-count"];

    private ScriptedModelServer? _model;
    private TestTelemetryCollector? _collector;

    public ScriptedModelServer Model => _model ?? throw new InvalidOperationException("The shared host has not booted yet.");
    internal TestTelemetryCollector Collector => _collector ?? throw new InvalidOperationException("The shared host has not booted yet.");

    protected override async Task<E2EBrain> StartHostAsync(CancellationToken cancellationToken)
    {
        _collector = TestTelemetryCollector.Start();
        _model = await ScriptedModelServer.StartAsync(cancellationToken);
        var brain = await IntoChatE2ETest.Create()
            .ConfigureModule<AIModule, AIOptions>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, _model.Endpoint))
            .ConfigureModule<FlutterModule, FlutterModuleOptions>(flutter => flutter.RunWebApp())
            .WithBrowser(new() { PrimarySession = false })
            // The one shared boot compiles the web shell; a cold CI runner needs well over the
            // default three minutes.
            .WithStartupTimeout(TimeSpan.FromMinutes(20))
            .WithResourceEnvironment(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = _collector.Endpoint.AbsoluteUri,
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
                ["DigitalBrain__Apps__ShipOnStartup"] = "true",
            })
            .StartAsync(cancellationToken);
        try
        {
            // Shipping is serial in the publisher; waiting here keeps the sandbox quiet while
            // facts run and lets any fact use a shipped app without its own wait.
            foreach (var package in ShippedPackages)
            { await IntoChatE2ETest.WaitUntilShippedAsync(brain, package, cancellationToken); }
        }
        catch
        {
            await brain.DisposeAsync();
            throw;
        }
        return brain;
    }

    protected override async ValueTask OnDisposedAsync()
    {
        if (_model is not null) { await _model.DisposeAsync(); }
        if (_collector is not null) { await _collector.DisposeAsync(); }
    }
}
