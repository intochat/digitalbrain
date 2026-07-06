using DigitalBrain.Core;
using DigitalBrain.Context;
using DigitalBrain.Kernel.Kernel;
using DigitalBrain.Kernel.Market;

namespace DigitalBrain.Kernel.Gateway;

public static class NeuronResolver
{
    public static INeuron Resolve(IGrainFactory grains, string neuronId)
    {
        if (string.IsNullOrWhiteSpace(neuronId))
            throw new ArgumentException("neuronId is required", nameof(neuronId));

        if (neuronId.StartsWith("task-", StringComparison.OrdinalIgnoreCase))
            return grains.GetGrain<IKernelTask>(neuronId);

        return neuronId switch
        {
            "aspire-main" => grains.GetGrain<IAspireNeuron>(neuronId),
            "automation-main" => grains.GetGrain<IAutomationNeuron>(neuronId),
            "closedloop-main" => grains.GetGrain<IClosedLoopNeuron>(neuronId),
            "context-main" => grains.GetGrain<IContextNeuron>(neuronId),
            "chart-main" => grains.GetGrain<IDataVisualizationNeuron>(neuronId),
            _ when neuronId.StartsWith("chart-", StringComparison.OrdinalIgnoreCase) => grains.GetGrain<IChartNeuron>(neuronId),
            "db-main" => grains.GetGrain<IDbSupportNeuron>(neuronId),
            "foundry-main" => grains.GetGrain<ICodeFoundryLoopNeuron>(neuronId),
            "ino-main" => grains.GetGrain<IInoNeuron>(neuronId),
            "llm-main" => grains.GetGrain<ILlmNeuron>(neuronId),
            "market-main" => grains.GetGrain<IMarketplaceNeuron>(neuronId),
            "market-data-main" => grains.GetGrain<IMarketDataNeuron>(neuronId),
            "session-main" => grains.GetGrain<IUserSessionNeuron>(neuronId),
            "status-main" => grains.GetGrain<ISystemStatus>(neuronId),
            _ => grains.GetGrain<IDemoNeuron>(neuronId)
        };
    }
}

