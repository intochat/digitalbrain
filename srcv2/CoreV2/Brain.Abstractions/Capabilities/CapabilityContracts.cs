using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;

namespace Brain.Abstractions.Capabilities;

public interface ICapability<TRequest, TResult>
    where TRequest : class
    where TResult : class;

public sealed record CapabilityDescriptor
{
    public CapabilityDescriptor(
        CapabilityId id,
        ContractId requestContract,
        ContractId resultContract,
        ModuleId owner,
        ContractVersion version)
    {
        Require(id.Value, nameof(id));
        Require(requestContract.Value, nameof(requestContract));
        Require(resultContract.Value, nameof(resultContract));
        Require(owner.Value, nameof(owner));
        RequirePositive(version, nameof(version));

        Id = id;
        RequestContract = requestContract;
        ResultContract = resultContract;
        Owner = owner;
        Version = version;
    }

    public CapabilityId Id { get; }

    public ContractId RequestContract { get; }

    public ContractId ResultContract { get; }

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

public interface ICapabilityBroker
{
    Task<TResult> UseAsync<TRequest, TResult>(
        CapabilityDescriptor capability,
        CapabilityUseName useName,
        TRequest request,
        ActivityContext context,
        CancellationToken cancellationToken)
        where TRequest : class
        where TResult : class;
}
