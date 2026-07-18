using TripRadar.DeploymentKit.Services;
using TripRadar.DeploymentKit.Settings;

namespace TripRadar.DeploymentKit.Interfaces;

public interface IPreDeploymentValidator
{
    PreDeploymentValidationResult ValidateAllResourceNames(InfrastructureSettings settings);
    
    PreDeploymentValidationResult ValidateSettings(InfrastructureSettings settings);
    
    Task<PreDeploymentValidationResult> ValidateAllAsync(InfrastructureSettings settings);
}



