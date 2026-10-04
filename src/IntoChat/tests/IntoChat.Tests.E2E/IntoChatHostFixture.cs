using DigitalBrain.AI;
using DigitalBrain.Flutter;

namespace IntoChat.Tests.E2E;

// Product-only shipped content; all lease, model, browser and telemetry support is shared.
public sealed class IntoChatHostFixture : ReferenceBrainFixture
{
    public static readonly string[] ShippedPackages = ["intochat/settings", "intochat/customer-researcher"];

    protected override Task<E2EBrain> StartBrainAsync(CancellationToken cancellationToken)
        => IntoChatE2ETest.Create()
            .ConfigureModule<AIModule, AIOptions>(ai => ai.WithModelEndpoint(AiProvider.OpenAI, Model.Endpoint))
            .ConfigureModule<FlutterModule, FlutterModuleOptions>(flutter => flutter.RunWebApp())
            .WithBrowser(new() { PrimarySession = false, AssertionTimeout = TimeSpan.FromMinutes(2) })
            .WithStartupTimeout(TimeSpan.FromMinutes(8))
            .WithResourceEnvironment(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = Collector.Endpoint.AbsoluteUri,
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
                ["DigitalBrain__Apps__ShipOnStartup"] = "settings,customer-researcher",
            })
            .StartAsync(cancellationToken);
}
