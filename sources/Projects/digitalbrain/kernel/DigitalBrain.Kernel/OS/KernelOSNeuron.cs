using DigitalBrain.Core;
using DigitalBrain.Runtime;
using DigitalBrain.Kernel.Runtime;
using Orleans.Journaling;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Runtime.Catalog;

namespace DigitalBrain.Kernel.OS;

public sealed class KernelOSNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    IServiceProvider serviceProvider,
    ILogger<KernelOSNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger), IKernelOSNeuron,
      IHandle<BootSystem>,
      IHandle<DiscoverNeuronsRequest>,
      IHandle<InitializeGateway>
{
    private readonly List<string> _transactionLogs = new();

    public async Task BootSystemAsync(BootSystem synapse)
    {
        await OnNextAsync(synapse);
    }

    public async Task HandleAsync(BootSystem synapse, CancellationToken ct)
    {
        logger.LogInformation("KernelOSNeuron received BootSystem synapse. Starting bootstrap transaction...");
        _transactionLogs.Add("BootSystem transaction started.");

        // 1. Fire DiscoverNeuronsRequest synapse to scan directories
        var scanHeader = SynapseFactory.CreateHeader<IKernelOSNeuron, IKernelOSNeuron>(
            new NeuronId("sys.os.kernel"),
            new NeuronId("sys.os.kernel")
        );
        var scanRequest = new DiscoverNeuronsRequest { Headers = scanHeader };
        await FireSynapseAsync(scanRequest, ct);

        // 2. Registers dynamic interpreted neuron paths
        logger.LogInformation("Step 2: Registering dynamic interpreted neuron paths...");
        _transactionLogs.Add("Registering dynamic interpreted neuron paths.");
        var registry = serviceProvider.GetRequiredService<InterpretedNeuronRegistry>();
        await registry.StartAsync(ct);

        // 3. Fire InitializeGateway synapse to spin up gateway listeners
        var gwHeader = SynapseFactory.CreateHeader<IKernelOSNeuron, IKernelOSNeuron>(
            new NeuronId("sys.os.kernel"),
            new NeuronId("sys.os.kernel")
        );
        var gwRequest = new InitializeGateway { Headers = gwHeader };
        await FireSynapseAsync(gwRequest, ct);

        logger.LogInformation("KernelOSNeuron BootSystem transaction completed successfully.");
        _transactionLogs.Add("BootSystem transaction completed successfully.");
    }

    public async Task HandleAsync(DiscoverNeuronsRequest synapse, CancellationToken ct)
    {
        logger.LogInformation("Step 1: Discovering assembly catalog neurons...");
        _transactionLogs.Add("Discovering neurons.");
        var scanner = serviceProvider.GetRequiredService<NeuronCatalogScanner>();
        await scanner.Execute(ct);
    }

    public async Task HandleAsync(InitializeGateway synapse, CancellationToken ct)
    {
        logger.LogInformation("Step 3: Activating gateway listeners...");
        _transactionLogs.Add("Activating gateway listeners.");
        var gateway = Grains.GetGrain<IGatewayNeuron>(Guid.Empty);
        await gateway.EnsureActivatedAsync();
    }
}
