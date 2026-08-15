using Brain.Abstractions.Context;
using Brain.Abstractions.Capabilities;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Operations;

namespace Brain.Abstractions.Policy;

public enum PolicyDecision
{
    Allowed,
    Refused,
    ConfirmationRequired,
}

public enum GraphChangeKind
{
    Install,
    Replace,
    Retire,
}

public sealed record GraphChangeRequest
{
    public GraphChangeRequest(
        GraphChangeKind kind,
        ModuleId requestedBy,
        ContractId contract,
        NeuronRoleId targetRole)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Require(requestedBy.Value, nameof(requestedBy));
        Require(contract.Value, nameof(contract));
        Require(targetRole.Value, nameof(targetRole));
        Kind = kind;
        RequestedBy = requestedBy;
        Contract = contract;
        TargetRole = targetRole;
    }

    public GraphChangeKind Kind { get; }

    public ModuleId RequestedBy { get; }

    public ContractId Contract { get; }

    public NeuronRoleId TargetRole { get; }

    private static void Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Graph change metadata is required.", parameterName);
        }
    }
}

public interface IWorkspacePolicyEvaluator
{
    PolicyDecision AuthorizeOperation(WorkspaceContext caller, OperationDescriptor operation);

    PolicyDecision AuthorizeGraphChange(ActivityContext context, GraphChangeRequest request);

    PolicyDecision AuthorizeCapability(ActivityContext context, CapabilityDescriptor capability);
}
