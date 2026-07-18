using DigitalBrain.Core;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Kernel.OS;

public sealed class KernelOSBootstrapper(
    IGrainFactory grains,
    ILogger<KernelOSBootstrapper> logger) : IStartupTask
{
    public async Task Execute(CancellationToken cancellationToken)
    {
        // Enforce license agreement check at the start of the lifecycle
        var licenseNeuron = grains.GetGrain<Runtime.Neurons.ILicenseNeuron>("global");
        await licenseNeuron.CheckLicenseAgreementAsync();

        var genesisNeuron = grains.GetGrain<IGenesisNeuron>(Guid.Empty);
        
        var metadata = SynapseFactory.CreateHeader<IGenesisNeuron, IGenesisNeuron>(
            new NeuronId("sys.host"),
            new NeuronId("sys.genesis")
        );
        var initSynapse = new InitializeGenesis("digitalbrain.ino") { Headers = metadata };
        
        logger.LogInformation("Firing InitializeGenesis synapse to GenesisNeuron...");
        await genesisNeuron.InitializeGenesisAsync(initSynapse);
        logger.LogInformation("KernelOSBootstrapper completed.");
    }
}
