using System.Globalization;
using DigitalBrain.Deployment;
using Pulumi;

namespace DigitalBrain.Memory;

// Runs the manifest's Qdrant container as an internal service with its data on Azure Files.
public sealed class MemoryDeployment : IDigitalBrainModuleDeployment
{
    private const int GrpcPort = 6334;
    private const int HttpPort = 6333;

    public void Deploy(ModuleDeploymentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var qdrant = context.Manifest.ServingContainer(context.Runtime, "memory-qdrant");
        var app = context.AddService(new ServiceDefinition("qdrant", context.Manifest.Image(qdrant), GrpcPort)
        {
            Transport = "http2",
            AdditionalPorts = [HttpPort],
            Secrets = new Dictionary<string, Input<string>> { ["QDRANT__SERVICE__API_KEY"] = context.Parameter(qdrant + "-Key") },
            DataPath = "/qdrant/storage",
        });
        var host = context.InternalHost(app);
        context.Provide(qdrant, "bindings.grpc.host", host);
        context.Provide(qdrant, "bindings.grpc.port", "443");
        context.Provide(qdrant, "bindings.grpc.url", host.Apply(name => $"https://{name}"));
        // Extra ports answer on the app name inside the environment, without TLS.
        context.Provide(qdrant, "bindings.http.host", app.Name);
        context.Provide(qdrant, "bindings.http.port", HttpPort.ToString(CultureInfo.InvariantCulture));
        context.Provide(qdrant, "bindings.http.url", app.Name.Apply(name => $"http://{name}:{HttpPort}"));
    }
}
