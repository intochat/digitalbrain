using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Runtime.Aspire;
using System.Collections.Concurrent;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Microsoft.Aspire.Runtime;

// First L3 SDK neuron target (E-SDK #59). Demonstrates the v3 §3 InoLang↔C#
// bridge for the SDK family: an .ino author writes
// `using $aspire = neuron(DigitalBrain.SDK.Aspire.Runtime)` and the runtime
// (ProductionNeuronHost) routes their `ask $aspire to "..."` through this
// grain. The [GrainType] FQN is the 1:1 identifier
// AssemblyScanningContractCatalog auto-discovers off the neuron scan and seeds
// as a ContractKind.Neuron schema — same pattern as the AI domain's
// LlmNeuron (#54), only this neuron lives on the SDK/L3 side of the carve-out
// rather than in a domain silo.
//
// v1 surface is intentionally tiny: `status` returns AspireConnectorStatus.Ok
// — the same string the boot connector commits to — so future expansion can
// add prompts without breaking the contract or the .ino scenario. Anything
// else returns a discoverability hint instead of failing, so authors get a
// useful response while exploring the neuron.
[GrainType(NeuronTargetFqn)]
[ImplicitStreamSubscription(nameof(IAspireRuntimeNeuron))]
internal sealed class AspireRuntimeNeuron(
    [FromKeyedServices("incoming")] Orleans.Journaling.IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] Orleans.Journaling.IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ILogger<AspireRuntimeNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger), ICallNeuronTarget, IAspireRuntimeNeuron, IHandle<ConfigureAspireResource>, IHandle<RestartResource>

{
    public const string NeuronTargetFqn = "SDK.Microsoft.Aspire";

    private readonly ConcurrentDictionary<string, string> _registeredResources = new(StringComparer.OrdinalIgnoreCase);

    public async Task<string> AskAsync(string prompt)
    {
        logger.LogInformation("AspireRuntimeNeuron received prompt: {Prompt}", prompt);

        if (string.Equals(prompt, "status", StringComparison.OrdinalIgnoreCase))
            return AspireConnectorStatus.Ok;

        // E-LAUNCH: Binds InoLang's `ask $aspire to "profile:local"` (which evaluates 
        // to "spawn-cluster profile:local" when lowered in BootHost's mapping loop) 
        // to the underlying OS/DCP connector launch sequence.
        if (prompt.StartsWith("spawn-cluster ", StringComparison.OrdinalIgnoreCase))
        {
            var profile = prompt["spawn-cluster ".Length..].Trim();
            
            // Resolve the connector using standard service locator on the Silo services
            var connector = this.ServiceProvider.GetRequiredService<IAspireBootConnector>();
            
            logger.LogInformation("Spawning Aspire DCP cluster with profile: {Profile}", profile);
            var result = await connector.SpawnClusterAsync(profile, CancellationToken.None);
            return result;
        }

        if (prompt.StartsWith("register-resource ", StringComparison.OrdinalIgnoreCase))
        {
            var remainder = prompt["register-resource ".Length..].Trim();
            var parts = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                var name = parts[0];
                _registeredResources[name] = remainder;
                logger.LogInformation("AspireRuntimeNeuron: Registered resource '{Name}' with specification: {Spec}", name, remainder);
                return $"registered resource {name} successfully";
            }
            return "invalid register-resource prompt";
        }

        if (prompt.Equals("list-resources", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join("; ", _registeredResources.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
        }

        if (prompt.StartsWith("restart resource ", StringComparison.OrdinalIgnoreCase))
        {
            var connector = this.ServiceProvider.GetRequiredService<IAspireBootConnector>();
            return await connector.RestartResourceAsync(prompt["restart resource ".Length..], CancellationToken.None);
        }

        if (prompt.StartsWith("spin up resource ", StringComparison.OrdinalIgnoreCase))
        {
            var connector = this.ServiceProvider.GetRequiredService<IAspireBootConnector>();
            return await connector.StartResourceAsync(prompt["spin up resource ".Length..], CancellationToken.None);
        }

        if (prompt.StartsWith("stop resource ", StringComparison.OrdinalIgnoreCase))
        {
            var connector = this.ServiceProvider.GetRequiredService<IAspireBootConnector>();
            return await connector.StopResourceAsync(prompt["stop resource ".Length..], CancellationToken.None);
        }

        if (string.Equals(prompt, "reload assemblies", StringComparison.OrdinalIgnoreCase))
        {
            var connector = this.ServiceProvider.GetRequiredService<IAspireBootConnector>();
            return await connector.RestartResourceAsync("kernel", CancellationToken.None);
        }

        return $"Unknown query '{prompt}'.";
    }

    public async Task HandleAsync(ConfigureAspireResource synapse, CancellationToken cancellationToken)
    {
        logger.LogInformation("AspireRuntimeNeuron received ConfigureAspireResource synapse: Name={Name}, Type={Type}", synapse.ResourceName, synapse.ResourceType);

        // Reconstruct the specification string and update _registeredResources
        var spec = $"{synapse.ResourceName} type:{synapse.ResourceType}";
        if (synapse.Config != null && synapse.Config.Count > 0)
        {
            spec += " " + string.Join(" ", synapse.Config.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
        }
        _registeredResources[synapse.ResourceName] = spec;

        // Resolve IAspireBootConnector
        var connector = this.ServiceProvider.GetRequiredService<IAspireBootConnector>();

        // Check autostart config key
        bool autostart = true;
        if (synapse.Config != null && synapse.Config.TryGetValue("autostart", out var autostartVal) && string.Equals(autostartVal, "false", StringComparison.OrdinalIgnoreCase))
        {
            autostart = false;
        }

        if (autostart)
        {
            logger.LogInformation("AspireRuntimeNeuron: Starting resource '{Name}'...", synapse.ResourceName);
            await connector.StartResourceAsync(synapse.ResourceName, cancellationToken);
        }
        else
        {
            logger.LogInformation("AspireRuntimeNeuron: autostart is false for '{Name}', skipping start.", synapse.ResourceName);
        }
    }

    public async Task HandleAsync(RestartResource synapse, CancellationToken cancellationToken)
    {
        logger.LogInformation("AspireRuntimeNeuron: Received RestartResource synapse for '{Name}'...", synapse.ResourceName);
        var connector = this.ServiceProvider.GetRequiredService<IAspireBootConnector>();
        await connector.RestartResourceAsync(synapse.ResourceName, cancellationToken);
    }
}


