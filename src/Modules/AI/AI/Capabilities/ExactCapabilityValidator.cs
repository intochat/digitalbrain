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
                        && !ContainsTerm(prompt, synapse.Description))
                    {
                        continue;
                    }

                    TryAdd(selected, seenTools, module, neuron, synapse, limit);
                }
            }
        }

        return selected;
    }

    private IEnumerable<ValidatedCapability> Expand(CapabilityCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

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
            description: synapse.Description,
            jsonSchema: synapse.JsonSchema,
            moduleId: module?.ModuleId.Value);
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

        var haystack = prompt.AsSpan();
        var needle = term.AsSpan().Trim();
        if (needle.IsEmpty || haystack.Length < needle.Length)
        {
            return false;
        }

        for (var index = 0; index <= haystack.Length - needle.Length; index++)
        {
            if (!haystack.Slice(index, needle.Length).Equals(needle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var beforeOk = index == 0 || TermSeparators.Contains(haystack[index - 1]);
            var afterIndex = index + needle.Length;
            var afterOk = afterIndex == haystack.Length || TermSeparators.Contains(haystack[afterIndex]);
            if (beforeOk && afterOk)
            {
                return true;
            }
        }

        return false;
    }
}
