using System.Collections.ObjectModel;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Graph;
using Brain.Abstractions.Identity;

namespace Brain.Abstractions.Wiring;

public enum WiringReadiness
{
    Ready,
    NeedsSetup,
    NeedsAuthorization,
    Unavailable,
}

public enum WiringActivationStatus
{
    Staging,
    Active,
    Failed,
}

public enum WiringPrerequisiteKind
{
    WorkspaceConfiguration,
    PrincipalAuthorization,
}

public sealed record WiringPolicyPrerequisite(string Id, WiringPrerequisiteKind Kind);

// This is an opaque declarative reference to a reshape that an installed module
// has registered. It intentionally carries no transformation delegate or data.
public sealed record WiringReshapeReference(
    ModuleId Owner,
    ContractId InputContract,
    ContractId OutputContract,
    string RegistrationId);

// A role slot describes a logical delivery. Runtime addresses and synapse keys
// are resolved only when the version is applied in a verified workspace context.
public sealed record WiringRoute(
    NeuronRoleId SourceRole,
    NeuronRoleId TargetRole,
    ContractId EventContract,
    WiringSlotId Slot,
    WiringReshapeReference? Reshape);

public sealed class WiringVersion
{
    public WiringVersion(
        WiringId wiring,
        int version,
        int? parentVersion,
        BrainActivityId causeActivity,
        OperationId operation,
        ContractVersion operationMajor,
        IReadOnlyCollection<WiringRoute> routes,
        IReadOnlyCollection<CapabilityId> requiredCapabilities,
        IReadOnlyCollection<WiringPolicyPrerequisite> policyPrerequisites)
    {
        if (wiring.Value == Guid.Empty || causeActivity.Value == Guid.Empty)
        {
            throw new ArgumentException("A wiring version requires a wiring and cause activity.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version, nameof(version));
        if (parentVersion is not null && (parentVersion <= 0 || parentVersion >= version))
        {
            throw new ArgumentOutOfRangeException(nameof(parentVersion));
        }

        Require(operation.Value, nameof(operation));
        if (operationMajor.Major <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(operationMajor));
        }

        Wiring = wiring;
        Version = version;
        ParentVersion = parentVersion;
        CauseActivity = causeActivity;
        Operation = operation;
        OperationMajor = operationMajor;
        var declaredRoutes = Copy(routes, nameof(routes), requireValue: true);
        EnsureDistinctStableRoutes(declaredRoutes, nameof(routes));
        Routes = declaredRoutes;
        RequiredCapabilities = Copy(requiredCapabilities, nameof(requiredCapabilities), requireValue: false);
        PolicyPrerequisites = Copy(policyPrerequisites, nameof(policyPrerequisites), requireValue: false);
    }

    public WiringId Wiring { get; }

    public int Version { get; }

    public int? ParentVersion { get; }

    public BrainActivityId CauseActivity { get; }

    public OperationId Operation { get; }

    public ContractVersion OperationMajor { get; }

    public IReadOnlyList<WiringRoute> Routes { get; }

    public IReadOnlyList<CapabilityId> RequiredCapabilities { get; }

    public IReadOnlyList<WiringPolicyPrerequisite> PolicyPrerequisites { get; }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyCollection<T> source, string parameterName, bool requireValue)
    {
        ArgumentNullException.ThrowIfNull(source, parameterName);
        if (requireValue && source.Count == 0)
        {
            throw new ArgumentException("A wiring version requires at least one route.", parameterName);
        }

        if (source.Any(static value => value is null))
        {
            throw new ArgumentException("A wiring declaration cannot contain null values.", parameterName);
        }

        return new ReadOnlyCollection<T>(source.ToArray());
    }

    private static void Require(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A wiring version requires a declared operation.", parameterName);
        }
    }

    private static void EnsureDistinctStableRoutes(IReadOnlyList<WiringRoute> routes, string parameterName)
    {
        var stableRoutes = new HashSet<StableRoute>();
        foreach (var route in routes)
        {
            if (!stableRoutes.Add(new StableRoute(route.SourceRole, route.EventContract, route.Slot)))
            {
                throw new ArgumentException(
                    "A wiring version cannot declare duplicate stable routes for the same source role, event contract, and slot.",
                    parameterName);
            }
        }
    }

    private readonly record struct StableRoute(
        NeuronRoleId SourceRole,
        ContractId EventContract,
        WiringSlotId Slot);
}

public sealed record WiringProposal(WiringVersion Version);

public sealed record WiringApplicability(WiringReadiness Readiness, string Explanation);

public sealed record WiringActivation(
    BrainActivityId Id,
    WiringId Wiring,
    int WiringVersion,
    WiringActivationStatus Status,
    int StagedShardCount,
    int RequiredShardCount);
