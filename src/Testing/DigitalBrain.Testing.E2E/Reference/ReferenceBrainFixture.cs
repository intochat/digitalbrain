using DigitalBrain.AI;
using DigitalBrain.Flutter;
using DigitalBrain.Testing.E2E.Agent;
using DigitalBrain.Testing.E2E.Diagnostics;

namespace DigitalBrain.Testing.E2E;

// The reference module composition plus a web shell, scripted model endpoint and OTLP collector. Facts isolate through their lease's WorkspaceId and
// fact-owned neuron ids; only a fact that needs a different composition boots its own host.
public class ReferenceBrainFixture : SharedBrainFixture, IAsyncLifetime
{
    // xUnit initializes the assembly fixture before any fact runs, so the boot (web shell
    // compile) never counts against a fact's timeout.
    public async ValueTask InitializeAsync() => await WarmUpAsync();

    private ScriptedModelServer? _model;
    private TestTelemetryCollector? _collector;

    public ScriptedModelServer Model => _model ?? throw new InvalidOperationException("The shared host has not booted yet.");
    public TestTelemetryCollector Collector => _collector ?? throw new InvalidOperationException("The shared host has not booted yet.");

    protected override async Task<E2EBrain> StartHostAsync(CancellationToken cancellationToken)
    {
        _collector = TestTelemetryCollector.Start();
        _model = await ScriptedModelServer.StartAsync(cancellationToken);
        return await StartBrainAsync(cancellationToken);
    }

    protected virtual Task<E2EBrain> StartBrainAsync(CancellationToken cancellationToken)
        => ReferenceBrain.Create()
            .ConfigureModule<AIModule, AIOptions>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, Model.Endpoint))
            .ConfigureModule<FlutterModule, FlutterModuleOptions>(flutter => flutter.RunWebApp())
            .WithBrowser(new() { PrimarySession = false, AssertionTimeout = TimeSpan.FromMinutes(2) })
            // The one shared boot compiles the web shell; a cold CI runner needs well over the
            // default three minutes.
            .WithStartupTimeout(TimeSpan.FromMinutes(20))
            .WithResourceEnvironment(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = Collector.Endpoint.AbsoluteUri,
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            })
            .StartAsync(cancellationToken);

    protected override async ValueTask OnDisposedAsync()
    {
        if (_model is not null) { await _model.DisposeAsync(); }
        if (_collector is not null) { await _collector.DisposeAsync(); }
    }
}
