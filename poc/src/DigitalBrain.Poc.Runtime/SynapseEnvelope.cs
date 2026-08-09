using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Poc.Abstractions;

namespace DigitalBrain.Poc.Runtime;

public sealed record SynapseEnvelope
{
    private SynapseEnvelope(
        string deliveryId,
        string ownerId,
        string contractAlias,
        Synapse synapse,
        CandidateFamilyId? candidateFamily,
        string? producingRevision,
        CandidateModuleIdentity? producingModuleIdentity,
        string? targetRevision,
        CandidateModuleIdentity? targetModuleIdentity,
        string? targetScope,
        string targetNeuronType)
    {
        DeliveryId = deliveryId;
        OwnerId = ownerId;
        ContractAlias = contractAlias;
        Synapse = synapse;
        CandidateFamily = candidateFamily;
        ProducingRevision = producingRevision;
        ProducingModuleIdentity = producingModuleIdentity;
        TargetRevision = targetRevision;
        TargetModuleIdentity = targetModuleIdentity;
        TargetScope = targetScope;
        TargetNeuronType = targetNeuronType;
    }

    public string DeliveryId { get; }

    public string OwnerId { get; }

    public string ContractAlias { get; }

    public Synapse Synapse { get; }

    public CandidateFamilyId? CandidateFamily { get; }

    public string? ProducingRevision { get; }

    public CandidateModuleIdentity? ProducingModuleIdentity { get; }

    public string? TargetRevision { get; }

    public CandidateModuleIdentity? TargetModuleIdentity { get; }

    public string? TargetScope { get; }

    public string TargetNeuronType { get; }

    internal static SynapseEnvelope Trusted(
        string ownerId,
        string inputReceiptId,
        Synapse synapse,
        RouteBinding route,
        int routeOrdinal) =>
        new(
            DeriveDeliveryId(inputReceiptId, route.Key, routeOrdinal),
            ownerId,
            route.ContractAlias,
            synapse,
            route.CandidateFamily,
            producingRevision: null,
            producingModuleIdentity: null,
            route.TargetRevision,
            route.TargetModuleIdentity,
            route.TargetScope,
            route.NeuronType);

    internal static SynapseEnvelope CandidateLocal(
        CandidateInvocationScope scope,
        Synapse synapse,
        string contractAlias,
        int outputOrdinal) =>
        new(
            DeriveDeliveryId(scope.InputDeliveryId, contractAlias, outputOrdinal),
            scope.OwnerId,
            contractAlias,
            synapse,
            scope.Family,
            scope.Revision,
            scope.ModuleIdentity,
            scope.Revision,
            scope.ModuleIdentity,
            targetScope: null,
            targetNeuronType: string.Empty);

    internal static SynapseEnvelope CandidateTrustedTarget(
        CandidateInvocationScope scope,
        Synapse synapse,
        string contractAlias,
        int outputOrdinal,
        string targetScope) =>
        new(
            DeriveDeliveryId(scope.InputDeliveryId, contractAlias, outputOrdinal),
            scope.OwnerId,
            contractAlias,
            synapse,
            scope.Family,
            scope.Revision,
            scope.ModuleIdentity,
            targetRevision: null,
            targetModuleIdentity: null,
            targetScope,
            targetNeuronType: string.Empty);

    internal static SynapseEnvelope Restore(
        string deliveryId,
        string ownerId,
        string contractAlias,
        Synapse synapse,
        CandidateFamilyId family,
        string? producingRevision,
        CandidateModuleIdentity? producingModuleIdentity,
        string targetRevision,
        CandidateModuleIdentity targetModuleIdentity,
        string targetNeuronType) =>
        new(
            deliveryId,
            ownerId,
            contractAlias,
            synapse,
            family,
            producingRevision,
            producingModuleIdentity,
            targetRevision,
            targetModuleIdentity,
            targetScope: null,
            targetNeuronType);

    internal static SynapseEnvelope RestoreTrustedTarget(
        string deliveryId,
        string ownerId,
        string contractAlias,
        Synapse synapse,
        CandidateFamilyId family,
        string producingRevision,
        CandidateModuleIdentity producingModuleIdentity,
        string targetScope) =>
        new(
            deliveryId,
            ownerId,
            contractAlias,
            synapse,
            family,
            producingRevision,
            producingModuleIdentity,
            targetRevision: null,
            targetModuleIdentity: null,
            targetScope,
            targetNeuronType: string.Empty);

    private static string DeriveDeliveryId(string inputIdentity, string targetIdentity, int ordinal)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{inputIdentity}\n{targetIdentity}\n{ordinal}"));
        return $"delivery-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }
}
