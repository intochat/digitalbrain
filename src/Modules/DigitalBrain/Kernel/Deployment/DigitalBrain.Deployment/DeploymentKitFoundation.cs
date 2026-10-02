using DeploymentKit.Deployer;
using DeploymentKit.Extensions;
using DeploymentKit.Infrastructure;
using DeploymentKit.Interfaces;
using DeploymentKit.Models.Outputs;
using DeploymentKit.Services;
using DeploymentKit.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Deployment;

// Runs DeploymentKit's orchestrator in the current Pulumi program.
internal static class DeploymentKitFoundation
{
    public static async Task<InfrastructureDeploymentOutputs> DeployAsync(DigitalBrainDeploymentOptions options, IReadOnlyList<IDigitalBrainModuleDeployment> modules)
    {
        var builder = InfrastructureDeployer.CreateBuilder()
            .SetName(options.Name).SetEnvironment(options.Environment).SetLocation(options.Location)
            .SetNamingPrefix(options.NamingPrefix).SetSubscriptionId(options.SubscriptionId)
            // The kit's defaults also build an Application Gateway and a VPN gateway, which the brain does not use.
            .AddNetworking(new NetworkSettings { EnableApplicationGateway = false, EnableVpnGateway = false, EnableDdosProtection = false, CustomDomain = null })
            .AddKeyVault().AddInsights().AddContainerRegistry().AddStorage()
            .SetValidationMode(options.Validation);
        var foundation = new FoundationContext(builder, options.Parameters, options.Manifest);
        foreach (var module in modules) { module.ConfigureFoundation(foundation); }
        var settings = await builder.BuildAsync().ConfigureAwait(false);

        // 1.0.0-preview.3 registers neither slot nor traffic management, so InfrastructureDeployer.DeployAsync
        // always fails its own service check; the orchestrator runs fine once both are registered.
        await using var services = new ServiceCollection()
            .AddInfrastructureServices(logging => logging.SetMinimumLevel(LogLevel.Warning))
            .AddScoped<ISlotManagementService, SlotManagementService>()
            .AddScoped<ITrafficManagementService, TrafficManagementService>()
            .BuildServiceProvider();
        return await services.GetRequiredService<InfrastructureOrchestrator>().DeployAsync(settings).ConfigureAwait(false);
    }
}
