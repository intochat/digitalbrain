using TripRadar.DeploymentKit.Models;
using TripRadar.DeploymentKit.Settings;

namespace TripRadar.DeploymentKit.Interfaces;

/// <summary>
/// Loads configuration for deployment orchestration from a specific source.
/// </summary>
public interface IInfrastructureConfigurationSource
{
    string SourceName { get; }

    Task<InfrastructureConfigurationSourceResult> LoadAsync(
        DeploymentOrchestratorOptions options,
        CancellationToken cancellationToken = default);
}

