using DeploymentKit.Interfaces;
using Pulumi;

namespace DigitalBrain.Deployment;

// Before anything is created: a module adds the DeploymentKit services it needs (a database, a cache).
public sealed class FoundationContext(IInfrastructureBuilder infrastructure, Config settings, AspireManifest manifest)
{
    public AspireManifest Manifest { get; } = manifest;

    public IInfrastructureBuilder Infrastructure { get; } = infrastructure;

    // Stack config: the manifest's parameters by their AppHost names, plus module deployment settings.
    public Config Settings { get; } = settings;
}
