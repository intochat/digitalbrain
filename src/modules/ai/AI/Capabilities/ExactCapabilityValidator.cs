using System.Globalization;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;

namespace DigitalBrain.AI;

public sealed class ExactCapabilityValidator
{
    private readonly ActiveCapabilityCatalog _catalog;

    public ExactCapabilityValidator(ActiveCapabilityCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    public IReadOnlyList<ValidatedCapability> Validate(
        IEnumerable<CapabilityCandidate> candidates,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var selected = new List<ValidatedCapability>();
        var seenTools = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            if (selected.Count >= limit)
            {
                break;
            }

            foreach (var capability in Expand(candidate))
            {
                if (selected.Count >= limit)
                {
                    break;
                }

                if (!seenTools.Add(capability.ToolName))
                {
                    continue;
                }

                selected.Add(capability);
            }
        }

        return selected;
    }

    public IReadOnlyList<ValidatedCapability> ResolveExactTerms(string prompt, int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var selected = new List<ValidatedCapability>();
        var seenTools = new HashSet<string>(StringComparer.Ordinal);

        foreach (var module in _catalog.Modules)
        {
            if (selected.Count >= limit)
            {
                break;
            }

            if (ContainsTerm(prompt, module.ModuleId.Value) || ContainsTerm(prompt, module.Description))
            {
                foreach (var neuron in module.Neurons)
                {
                    AddAccepted(selected, seenTools, module, neuron, limit);
                }

                continue;
            }

            foreach (var neuron in module.Neurons)
            {
                if (selected.Count >= limit)
                {
                    break;
                }

                if (ContainsTerm(prompt, neuron.ContractId) || ContainsTerm(prompt, neuron.Description))
                {
                    AddAccepted(selected, seenTools, module, neuron, limit);
                    continue;
                }

                foreach (var synapse in neuron.Accepted)
                {
                    if (selected.Count >= limit)
                    {
                        break;
                    }

                    if (!ContainsTerm(prompt, synapse.ContractId)
                        && !ContainsTerm(prompt, synapse.Description)
                        && !synapse.Examples.Any(example => ContainsTerm(prompt, example)))
                    {
                        continue;
                    }

                    TryAdd(selected, seenTools, module, neuron, synapse, limit);
                }
            }
        }

        foreach (var behavior in _catalog.Behaviors)
        {
            if (selected.Count >= limit)
            {
                break;
            }

            if (!ContainsTerm(prompt, behavior.BehaviorId)
                && !ContainsTerm(prompt, behavior.DisplayName)
                && !ContainsTerm(prompt, behavior.Description)
                && !behavior.ScenarioTitles.Any(title => ContainsTerm(prompt, title)))
            {
                continue;
            }

            var capability = ToBehaviorCapability(behavior);
            if (seenTools.Add(capability.ToolName))
            {
                selected.Add(capability);
            }
        }

        return selected;
    }

    private IEnumerable<ValidatedCapability> Expand(CapabilityCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (string.Equals(candidate.Kind, CapabilityKinds.Behavior, StringComparison.Ordinal)
            || !string.IsNullOrWhiteSpace(candidate.BehaviorId))
        {
            var behaviorId = candidate.BehaviorId ?? candidate.ContractId;
            if (_catalog.TryGetBehavior(behaviorId, out var behavior) && behavior is not null)
            {
                if (!string.IsNullOrWhiteSpace(candidate.ArtifactHash)
                    && !string.Equals(candidate.ArtifactHash, behavior.ArtifactHash, StringComparison.Ordinal))
                {
                    yield break;
                }

                yield return ToBehaviorCapability(behavior);
            }

            yield break;
        }

        if (string.Equals(candidate.Kind, CapabilityKinds.Module, StringComparison.Ordinal))
        {
            if (!_catalog.TryGetModule(new ModuleId(candidate.ContractId), out var module) || module is null)
            {
                yield break;
            }

            foreach (var neuron in module.Neurons)
            {
                foreach (var synapse in neuron.Accepted)
                {
                    var validated = TryCreate(module, neuron, synapse);
                    if (validated is not null)
                    {
                        yield return validated;
                    }
                }
            }

            yield break;
        }

        if (string.Equals(candidate.Kind, CapabilityKinds.Neuron, StringComparison.Ordinal))
        {
            if (!_catalog.TryGetNeuron(candidate.ContractId, out var neuron) || neuron is null)
            {
                yield break;
            }

            var owningModule = FindModuleForNeuron(neuron.ContractId);
            foreach (var synapse in neuron.Accepted)
            {
                var validated = TryCreate(owningModule, neuron, synapse);
                if (validated is not null)
                {
                    yield return validated;
                }
            }

            yield break;
        }

        if (string.Equals(candidate.Kind, CapabilityKinds.Synapse, StringComparison.Ordinal)
            || candidate.SchemaVersion is not null)
        {
            if (candidate.SchemaVersion is not int schemaVersion)
            {
                yield break;
            }

            if (!_catalog.TryGetSynapse(candidate.ContractId, schemaVersion, out var synapse)
                || synapse is null)
            {
                yield break;
            }

            if (!TryResolveAcceptedHost(candidate, synapse, out var module, out var neuron)
                || neuron is null)
            {
                yield break;
            }

            var validated = TryCreate(module, neuron, synapse);
            if (validated is not null)
            {
                yield return validated;
            }
        }
    }

    private bool TryResolveAcceptedHost(
        CapabilityCandidate candidate,
        SynapseCapabilityDescriptor synapse,
        out CapabilityManifest? module,
        out NeuronCapabilityDescriptor? neuron)
    {
        module = null;
        neuron = null;

        if (!string.IsNullOrWhiteSpace(candidate.NeuronContractId)
            && _catalog.TryGetNeuron(candidate.NeuronContractId, out neuron)
            && neuron is not null
            && neuron.Accepted.Any(item =>
                string.Equals(item.ContractId, synapse.ContractId, StringComparison.Ordinal)
                && item.SchemaVersion == synapse.SchemaVersion))
        {
            module = FindModuleForNeuron(neuron.ContractId);
            return true;
        }

        foreach (var active in _catalog.Modules)
        {
            foreach (var host in active.Neurons)
            {
                if (!host.Accepted.Any(item =>
                        string.Equals(item.ContractId, synapse.ContractId, StringComparison.Ordinal)
                        && item.SchemaVersion == synapse.SchemaVersion))
                {
                    continue;
                }

                module = active;
                neuron = host;
                return true;
            }
        }

        return false;
    }

    private CapabilityManifest? FindModuleForNeuron(string neuronContractId)
    {
        foreach (var module in _catalog.Modules)
        {
            if (module.Neurons.Any(neuron =>
                    string.Equals(neuron.ContractId, neuronContractId, StringComparison.Ordinal)))
            {
                return module;
            }
        }

        return null;
    }

    private static void AddAccepted(
        List<ValidatedCapability> selected,
        HashSet<string> seenTools,
        CapabilityManifest module,
        NeuronCapabilityDescriptor neuron,
        int limit)
    {
        foreach (var synapse in neuron.Accepted)
        {
            TryAdd(selected, seenTools, module, neuron, synapse, limit);
        }
    }

    private static void TryAdd(
        List<ValidatedCapability> selected,
        HashSet<string> seenTools,
        CapabilityManifest module,
        NeuronCapabilityDescriptor neuron,
        SynapseCapabilityDescriptor synapse,
        int limit)
    {
        if (selected.Count >= limit)
        {
            return;
        }

        var capability = TryCreate(module, neuron, synapse);
        if (capability is null || !seenTools.Add(capability.ToolName))
        {
            return;
        }

        selected.Add(capability);
    }

    private static ValidatedCapability? TryCreate(
        CapabilityManifest? module,
        NeuronCapabilityDescriptor neuron,
        SynapseCapabilityDescriptor synapse)
    {
        if (!neuron.Accepted.Any(item =>
                string.Equals(item.ContractId, synapse.ContractId, StringComparison.Ordinal)
                && item.SchemaVersion == synapse.SchemaVersion))
        {
            return null;
        }

        return new ValidatedCapability(
            kind: CapabilityKinds.Synapse,
            toolName: ValidatedCapability.ToolNameFor(synapse.ContractId, synapse.SchemaVersion),
            contractId: synapse.ContractId,
            schemaVersion: synapse.SchemaVersion,
            neuronContractId: neuron.ContractId,
            defaultInstanceName: neuron.DefaultInstanceName,
            description: BuildDescription(synapse),
            jsonSchema: synapse.JsonSchema,
            examples: synapse.Examples,
            moduleId: module?.ModuleId.Value);
    }

    private static ValidatedCapability ToBehaviorCapability(ActiveBehaviorCapability behavior)
    {
        var description = behavior.Description;
        if (behavior.ScenarioTitles.Count > 0)
        {
            description = string.Create(
                CultureInfo.InvariantCulture,
                $"{behavior.Description} Scenarios: {string.Join("; ", behavior.ScenarioTitles)}.");
        }

        return new ValidatedCapability(
            kind: CapabilityKinds.Behavior,
            toolName: ValidatedCapability.ToolNameFor(behavior.BehaviorId, schemaVersion: 1),
            contractId: behavior.BehaviorId,
            schemaVersion: 1,
            neuronContractId: behavior.NeuronContractId,
            defaultInstanceName: behavior.InstanceName,
            description: description,
            jsonSchema: behavior.JsonSchema,
            examples: behavior.ScenarioTitles,
            behaviorId: behavior.BehaviorId,
            artifactHash: behavior.ArtifactHash);
    }

    private static string BuildDescription(SynapseCapabilityDescriptor synapse)
    {
        if (synapse.Examples.Count == 0)
        {
            return synapse.Description;
        }

        var examples = string.Join("; ", synapse.Examples.Where(static example => !string.IsNullOrWhiteSpace(example)));
        return string.IsNullOrWhiteSpace(examples)
            ? synapse.Description
            : string.Create(CultureInfo.InvariantCulture, $"{synapse.Description} Examples: {examples}.");
    }

    private static readonly char[] TermSeparators =
    [
        ' ', '\t', '\r', '\n', '.', ',', ';', ':', '/', '\\', '-', '_', '@',
        '(', ')', '[', ']', '{', '}', '"', '\'',
    ];

    private static bool ContainsTerm(string prompt, string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return false;
        }

        if (prompt.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Catalog descriptions are long; match substantial tokens so "Gmail" hits
        // "Intent-level Gmail request..." without requiring the full description in the prompt.
        foreach (var token in term.Split(TermSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length < 4)
            {
                continue;
            }

            if (prompt.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
