using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Memory;

namespace DigitalBrain.AI;

public sealed class VectorMemoryCapabilitySearch : ICapabilityCandidateSearch
{
    internal static readonly TimeSpan SearchBound = TimeSpan.FromSeconds(10);

    private readonly IGrainFactory _grains;
    private readonly string _memoryInstanceName;

    public VectorMemoryCapabilitySearch(IGrainFactory grains, string memoryInstanceName = "default")
    {
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryInstanceName);
        _grains = grains;
        _memoryInstanceName = memoryInstanceName;
    }

    [SuppressMessage(
        "Usage",
        "CA1849:Call async methods when in an async method",
        Justification = "DigitalBrainClient.Connect is the in-silo factory; ConnectAsync is the behavior-worker surface.")]
    public async Task<IReadOnlyList<CapabilityCandidate>> SearchAsync(
        OwnerId owner,
        string prompt,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        cancellationToken.ThrowIfCancellationRequested();

        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(SearchBound);

        try
        {
            var brain = DigitalBrainClient.Connect(_grains, owner.Value);
            var memory = brain.Get<IVectorMemory>(_memoryInstanceName);
            var perNamespace = Math.Max(limit, 1);
            var candidates = new List<CapabilityCandidate>();

            await AppendAsync(
                    candidates,
                    memory,
                    VectorMemoryNamespace.Capabilities,
                    prompt,
                    perNamespace,
                    bound.Token)
                .ConfigureAwait(false);
            await AppendAsync(
                    candidates,
                    memory,
                    VectorMemoryNamespace.Behaviors,
                    prompt,
                    perNamespace,
                    bound.Token)
                .ConfigureAwait(false);

            return candidates;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Vector memory capability search did not complete within {SearchBound.TotalSeconds} seconds.");
        }
    }

    private static async Task AppendAsync(
        List<CapabilityCandidate> candidates,
        NeuronReference<IVectorMemory> memory,
        VectorMemoryNamespace @namespace,
        string prompt,
        int limit,
        CancellationToken cancellationToken)
    {
        var matches = await memory
            .SendAsync(new SearchVectorMemory(@namespace, prompt, limit, Metadata: null), cancellationToken)
            .ConfigureAwait(false);

        foreach (var match in matches.Matches)
        {
            candidates.Add(ToCandidate(match));
        }
    }

    private static CapabilityCandidate ToCandidate(VectorMemoryMatch match)
    {
        match.Metadata.TryGetValue(VectorProjectionMetadataKeys.Kind, out var kind);
        match.Metadata.TryGetValue(VectorProjectionMetadataKeys.ContractId, out var contractId);
        match.Metadata.TryGetValue(VectorProjectionMetadataKeys.ModuleId, out var moduleId);
        match.Metadata.TryGetValue(VectorProjectionMetadataKeys.NeuronContractId, out var neuronContractId);
        match.Metadata.TryGetValue(VectorProjectionMetadataKeys.BehaviorId, out var behaviorId);
        match.Metadata.TryGetValue(VectorProjectionMetadataKeys.ArtifactHash, out var artifactHash);
        match.Metadata.TryGetValue(VectorProjectionMetadataKeys.SchemaVersion, out var schemaText);

        int? schemaVersion = null;
        if (!string.IsNullOrWhiteSpace(schemaText)
            && int.TryParse(schemaText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed >= 1)
        {
            schemaVersion = parsed;
        }

        return new CapabilityCandidate(
            Kind: string.IsNullOrWhiteSpace(kind) ? CapabilityKinds.Synapse : kind,
            ContractId: string.IsNullOrWhiteSpace(contractId) ? match.Key : contractId,
            SchemaVersion: schemaVersion,
            ModuleId: moduleId,
            NeuronContractId: neuronContractId,
            BehaviorId: behaviorId,
            ArtifactHash: artifactHash,
            SourceKey: match.Key);
    }
}
