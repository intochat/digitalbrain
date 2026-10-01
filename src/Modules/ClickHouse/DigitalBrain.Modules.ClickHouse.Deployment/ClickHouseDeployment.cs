using DigitalBrain.Deployment;
using Pulumi;

namespace DigitalBrain.ClickHouse;

// Runs the manifest's ClickHouse container as an internal service with its data on Azure Files.
// Seed scripts the AppHost copies into a local container do not travel with the manifest.
public sealed class ClickHouseDeployment : IDigitalBrainModuleDeployment
{
    private const int HttpPort = 8123;

    public void Deploy(ModuleDeploymentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var server = context.Manifest.ServingContainer(context.Runtime, ClickHouseRegistration.DefaultConnectionName);
        var app = context.AddService(new ServiceDefinition("clickhouse", context.Manifest.Image(server), HttpPort)
        {
            Transport = "http",
            AllowInsecure = true,
            Environment = new Dictionary<string, Input<string>> { ["CLICKHOUSE_USER"] = "default" },
            Secrets = new Dictionary<string, Input<string>> { ["CLICKHOUSE_PASSWORD"] = context.Parameter(server + "-password") },
            DataPath = "/var/lib/clickhouse",
            Cpu = 2,
            Memory = "4Gi",
        });
        context.Provide(server, "bindings.http.host", context.InternalHost(app));
        context.Provide(server, "bindings.http.port", "80");
    }
}
