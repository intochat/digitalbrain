using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;

namespace Brain.Abstractions.Activities;

public enum ActivityStatus
{
    Accepted,
    Running,
    Succeeded,
    Failed,
}

public readonly record struct ActivityPayloadReference
{
    public ActivityPayloadReference(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record ActivityProgressReference
{
    public ActivityProgressReference(ContractId contract, ActivityPayloadReference payload)
    {
        RequireContract(contract, nameof(contract));
        RequirePayload(payload, nameof(payload));
        Contract = contract;
        Payload = payload;
    }

    public ContractId Contract { get; }

    public ActivityPayloadReference Payload { get; }

    private static void RequireContract(ContractId contract, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(contract.Value))
        {
            throw new ArgumentException("An activity reference requires a contract.", parameterName);
        }
    }

    private static void RequirePayload(ActivityPayloadReference payload, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(payload.Value))
        {
            throw new ArgumentException("An activity reference requires a payload reference.", parameterName);
        }
    }
}

public sealed record ActivityResultReference
{
    public ActivityResultReference(ContractId contract, ActivityPayloadReference payload)
    {
        if (string.IsNullOrWhiteSpace(contract.Value))
        {
            throw new ArgumentException("An activity reference requires a contract.", nameof(contract));
        }

        if (string.IsNullOrWhiteSpace(payload.Value))
        {
            throw new ArgumentException("An activity reference requires a payload reference.", nameof(payload));
        }

        Contract = contract;
        Payload = payload;
    }

    public ContractId Contract { get; }

    public ActivityPayloadReference Payload { get; }
}

public sealed record ActivityProblem(string Code, string Summary);

public sealed record ActivityView(
    BrainActivityId Activity,
    OperationId Operation,
    ActivityStatus Status,
    ContractId TerminalResultContract,
    ActivityProgressReference? Progress,
    ActivityResultReference? Result,
    ActivityProblem? Problem)
{
    public static ActivityView Accepted(
        BrainActivityId activity,
        OperationId operation,
        ContractId terminalResultContract)
        => new(activity, operation, ActivityStatus.Accepted, terminalResultContract, null, null, null);
}

public sealed record ActivityProgress<T>(T Value)
    where T : class;

public sealed record ActivityResult<T>(T Value)
    where T : class;

public interface IActivityPayloadReader
{
    Task<ActivityProgress<T>> ReadProgressAsync<T>(
        ActivityProgressReference reference,
        WorkspaceContext caller,
        CancellationToken cancellationToken)
        where T : class;

    Task<ActivityResult<T>> ReadResultAsync<T>(
        ActivityResultReference reference,
        WorkspaceContext caller,
        CancellationToken cancellationToken)
        where T : class;
}
