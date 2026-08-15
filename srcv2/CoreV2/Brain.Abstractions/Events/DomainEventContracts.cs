using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;

namespace Brain.Abstractions.Events;

public interface IDomainEvent;

public enum EventVisibility
{
    Internal,
    Published,
}

public sealed record EventDescriptor
{
    public EventDescriptor(
        ContractId contract,
        ModuleId owner,
        Type eventType,
        EventVisibility visibility)
    {
        Require(contract.Value, nameof(contract));
        Require(owner.Value, nameof(owner));
        ArgumentNullException.ThrowIfNull(eventType);

        if (!eventType.IsClass || eventType.IsAbstract || !typeof(IDomainEvent).IsAssignableFrom(eventType))
        {
            throw new ArgumentException(
                "An event descriptor requires a concrete domain event CLR type.",
                nameof(eventType));
        }

        Contract = contract;
        Owner = owner;
        EventType = eventType;
        Visibility = visibility;
    }

    public ContractId Contract { get; }

    public ModuleId Owner { get; }

    public Type EventType { get; }

    public EventVisibility Visibility { get; }

    private static void Require(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A descriptor dependency is required.", parameterName);
        }
    }
}
