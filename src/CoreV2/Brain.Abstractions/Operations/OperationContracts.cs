using Brain.Abstractions.Activities;
using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;

namespace Brain.Abstractions.Operations;

public interface IOperation<TInput, TResult>
    where TInput : class
    where TResult : class;

public sealed record OperationDescriptor
{
    public OperationDescriptor(
        OperationId id,
        ContractId inputContract,
        ContractId terminalResultContract,
        NeuronRoleId entryRole,
        ModuleId owner,
        ContractVersion version)
    {
        Require(id.Value, nameof(id));
        Require(inputContract.Value, nameof(inputContract));
        Require(terminalResultContract.Value, nameof(terminalResultContract));
        Require(entryRole.Value, nameof(entryRole));
        Require(owner.Value, nameof(owner));
        RequirePositive(version, nameof(version));

        if (inputContract == terminalResultContract)
        {
            throw new ArgumentException(
                "An operation input contract and terminal result contract must be distinct.",
                nameof(terminalResultContract));
        }

        Id = id;
        InputContract = inputContract;
        TerminalResultContract = terminalResultContract;
        EntryRole = entryRole;
        Owner = owner;
        Version = version;
    }

    public OperationId Id { get; }

    public ContractId InputContract { get; }

    public ContractId TerminalResultContract { get; }

    public NeuronRoleId EntryRole { get; }

    public ModuleId Owner { get; }

    public ContractVersion Version { get; }

    private static void Require(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A descriptor dependency is required.", parameterName);
        }
    }

    private static void RequirePositive(ContractVersion version, string parameterName)
    {
        if (version.Major <= 0)
        {
            throw new ArgumentException("A descriptor version must have a positive major value.", parameterName);
        }
    }
}

public sealed record OperationInvocation<TInput>(
    OperationDescriptor Operation,
    TInput Input,
    WorkspaceContext Caller,
    IdempotencyKey IdempotencyKey)
    where TInput : class;

public sealed record OperationAccepted(BrainActivityId Activity);

public interface IOperationGateway
{
    Task<OperationAccepted> InvokeAsync<TInput, TResult>(
        OperationDescriptor operation,
        TInput input,
        WorkspaceContext caller,
        IdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
        where TInput : class
        where TResult : class;

    Task<ActivityView> ObserveAsync(
        BrainActivityId activity,
        WorkspaceContext caller,
        CancellationToken cancellationToken);
}
