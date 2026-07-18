using DigitalBrain.Core;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.SDK.Microsoft.Aspire.Runtime;
using Orleans.Journaling;

namespace DigitalBrain.Kernel.OS;

[GrainType("DigitalBrain.Kernel.OS.GenesisNeuron")]
public sealed class GenesisNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ILogger<GenesisNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger), IGenesisNeuron,
      IHandle<InitializeGenesis>
{
    public async Task InitializeGenesisAsync(InitializeGenesis synapse)
    {
        await OnNextAsync(synapse);
    }

    public async Task HandleAsync(InitializeGenesis synapse, CancellationToken ct)
    {
        logger.LogInformation("GenesisNeuron: system bootstrapping initialized. SPEC path: {TopologyPath}", synapse.TopologyPath);

        // 1. Ensure the primary Orleans brain context exists
        var registry = Grains.GetGrain<DigitalBrain.Runtime.Brain.IBrainRegistry>(Guid.Empty);
        var existing = await registry.ListBrainsAsync();
        if (!existing.Any(b => string.Equals(b.BrainId, "primary", StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogInformation("genesis: ensuring primary brain exists");
            await registry.CreateBrainAsync("Primary");
        }

        // 2. Load and parse digitalbrain.ino topology configuration
        var path = synapse.TopologyPath;
        if (!Path.IsPathRooted(path))
        {
            var baseDir = AppContext.BaseDirectory;
            var candidate = Path.Combine(baseDir, path);
            if (!File.Exists(candidate))
            {
                candidate = Path.Combine(Directory.GetCurrentDirectory(), path);
            }
            if (File.Exists(candidate))
            {
                path = candidate;
            }
        }

        if (File.Exists(path))
        {
            logger.LogInformation("GenesisNeuron: Reading topology spec from {Path}", path);
            var lines = await File.ReadAllLinesAsync(path, ct);
            var aspireNeuron = Grains.GetGrain<ICallNeuronTarget>("SDK.Microsoft.Aspire");

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Contains("register-resource", StringComparison.OrdinalIgnoreCase))
                {
                    // Find the prompt inside quotes if any, or use the whole string
                    string prompt = trimmed;
                    int quoteStart = trimmed.IndexOf('"');
                    int quoteEnd = trimmed.LastIndexOf('"');
                    if (quoteStart != -1 && quoteEnd > quoteStart)
                    {
                        prompt = trimmed[(quoteStart + 1)..quoteEnd];
                    }

                    logger.LogInformation("GenesisNeuron: Dynamic registering resource: {Prompt}", prompt);
                    
                    try
                    {
                        // Invoke AspireRuntimeNeuron for dynamic registration
                        await aspireNeuron.AskAsync(prompt);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "GenesisNeuron: Failed to register resource: {Prompt}", prompt);
                    }

                    // Parse components for ConfigureAspireResource synapse dispatch
                    var parsed = ParseRegisterResource(prompt);
                    var header = SynapseFactory.CreateHeader<IGenesisNeuron, IAspireRuntimeNeuron>(
                        new NeuronId("sys.genesis"),
                        new NeuronId("sys.aspire")
                    );
                    var configureSynapse = new ConfigureAspireResource(parsed.Name, parsed.Type, parsed.Config) { Headers = header };
                    await FireSynapseAsync(configureSynapse, ct);
                }
            }
        }
        else
        {
            logger.LogWarning("GenesisNeuron: Topology spec file not found at: {Path}", path);
        }

        // 3. Dispatch ConfigureAiSubsystem synapse
        var aiHeader = SynapseFactory.CreateHeader<IGenesisNeuron, IGenesisNeuron>(
            new NeuronId("sys.genesis"),
            new NeuronId("sys.ai")
        );
        var aiSynapse = new ConfigureAiSubsystem(["OpenAI", "Grok"], "TextEmbedding3Small", "LargeV3Turbo") { Headers = aiHeader };
        await FireSynapseAsync(aiSynapse, ct);

        // 4. Trigger KernelOSNeuron BootSystem to finish core VM boot sequence
        var osNeuron = Grains.GetGrain<IKernelOSNeuron>(Guid.Empty);
        var bootHeader = SynapseFactory.CreateHeader<IGenesisNeuron, IKernelOSNeuron>(
            new NeuronId("sys.genesis"),
            new NeuronId("sys.os.kernel")
        );
        var bootSynapse = new BootSystem { Headers = bootHeader };
        logger.LogInformation("GenesisNeuron: delegating to KernelOSNeuron to complete VM boot...");
        await osNeuron.BootSystemAsync(bootSynapse);

        logger.LogInformation("GenesisNeuron: system bootstrapping flow completed successfully.");
    }

    private static (string Name, string Type, Dictionary<string, string> Config) ParseRegisterResource(string prompt)
    {
        var clean = prompt.Trim().Trim('"');
        if (clean.StartsWith("register-resource ", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean["register-resource ".Length..];
        }
        
        var spaceIndex = clean.IndexOf(' ');
        if (spaceIndex == -1)
        {
            return (clean, string.Empty, new Dictionary<string, string>());
        }
        
        var name = clean[..spaceIndex].Trim();
        var remainder = clean[spaceIndex..].Trim();
        
        var config = new Dictionary<string, string>();
        var type = string.Empty;
        
        var keys = new[] { "type:", "port:", "path:", "args:", "autostart:" };
        var indices = new List<(string Key, int Index)>();
        foreach (var key in keys)
        {
            int idx = remainder.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx != -1)
            {
                indices.Add((key, idx));
            }
        }
        
        indices = [.. indices.OrderBy(x => x.Index)];
        
        for (int i = 0; i < indices.Count; i++)
        {
            var current = indices[i];
            int start = current.Index + current.Key.Length;
            int end = (i + 1 < indices.Count) ? indices[i + 1].Index : remainder.Length;
            
            var val = remainder[start..end].Trim();
            var keyName = current.Key.TrimEnd(':');
            
            if (keyName.Equals("type", StringComparison.OrdinalIgnoreCase))
            {
                type = val;
            }
            else
            {
                config[keyName] = val;
            }
        }
        
        return (name, type, config);
    }
}
