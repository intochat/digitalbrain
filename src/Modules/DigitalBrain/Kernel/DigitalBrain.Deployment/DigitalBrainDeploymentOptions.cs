using DeploymentKit.Enums;
using Pulumi;

namespace DigitalBrain.Deployment;

public sealed record DigitalBrainDeploymentOptions
{
    public required AspireManifest Manifest { get; init; }
    // Module parameters are stack config under this namespace, named as the AppHost declared them.
    public required Config Parameters { get; init; }
    public required string SubscriptionId { get; init; }
    public required string RuntimeImage { get; init; }
    public string Name { get; init; } = "digitalbrain";
    public string Environment { get; init; } = "production";
    public string Location { get; init; } = "westeurope";
    public string NamingPrefix { get; init; } = "dbrain";
    public ValidationMode Validation { get; init; } = ValidationMode.Full;
    // Resolved from the manifest's module list through [ModuleDeployment]; tests may pass their own.
    public IReadOnlyList<IDigitalBrainModuleDeployment>? Modules { get; init; }
}
