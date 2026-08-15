using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Reshapes;
using Brain.Core.Modules;
using Brain.Core.Outbox;

namespace Brain.Core.Reshapes;

internal interface IReshapeRegistry
{
    void Validate(DeliverySnapshot snapshot, IDomainEvent source);

    IDomainEvent Transform(DeliverySnapshot snapshot, IDomainEvent source);
}

internal sealed class ReshapeRegistry(ModuleSet modules) : IReshapeRegistry
{
    private readonly ModuleSet _modules = modules ?? throw new ArgumentNullException(nameof(modules));
    private readonly Dictionary<ReshapeId, IRegisteredReshape> _registered = [];

    internal void Register<TFrom, TTo>(
        ReshapeId id,
        ReshapeDescriptor descriptor,
        IReshape<TFrom, TTo> reshape)
        where TFrom : IDomainEvent
        where TTo : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(reshape);
        ValidateDeclaration(descriptor, typeof(TFrom), typeof(TTo));
        if (!_registered.TryAdd(id, new RegisteredReshape<TFrom, TTo>(descriptor, reshape)))
        {
            throw new InvalidOperationException($"Reshape '{id.Value}' is already registered.");
        }
    }

    public void Validate(DeliverySnapshot snapshot, IDomainEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var registered = Resolve(snapshot);
        ValidateSnapshot(snapshot, registered.Descriptor);
        ValidateCrossModulePublication(snapshot, registered.Descriptor);
        registered.ValidateSource(source);
    }

    public IDomainEvent Transform(DeliverySnapshot snapshot, IDomainEvent source)
    {
        Validate(snapshot, source);
        return Resolve(snapshot).Transform(source);
    }

    private IRegisteredReshape Resolve(DeliverySnapshot snapshot)
    {
        if (snapshot.Reshape is not { } reshape)
        {
            throw new InvalidOperationException("A reshape snapshot requires a registered reshape id.");
        }

        return _registered.TryGetValue(reshape, out var registered)
            ? registered
            : throw new InvalidOperationException($"No reshape is registered for '{reshape.Value}'.");
    }

    private void ValidateDeclaration(ReshapeDescriptor descriptor, Type from, Type to)
    {
        var owner = _modules.Modules.SingleOrDefault(manifest => manifest.Id == descriptor.Owner)
            ?? throw new InvalidOperationException($"Reshape owner '{descriptor.Owner}' is not installed.");
        if (!owner.Reshapes.Contains(descriptor))
        {
            throw new InvalidOperationException("A reshape must be explicitly declared by its owner manifest.");
        }

        var input = Event(descriptor.InputEvent);
        var output = Event(descriptor.OutputEvent);
        if (input.Owner != descriptor.Owner || output.Owner != descriptor.Owner
            || input.EventType != from || output.EventType != to)
        {
            throw new InvalidOperationException("A reshape registration must match its declared typed input and output events.");
        }
    }

    private void ValidateSnapshot(DeliverySnapshot snapshot, ReshapeDescriptor descriptor)
    {
        snapshot.EnsureValid();
        if (snapshot.InputContract != descriptor.InputEvent || snapshot.OutputContract != descriptor.OutputEvent)
        {
            throw new InvalidOperationException("A delivery snapshot contracts must match the registered reshape declaration.");
        }
    }

    private void ValidateCrossModulePublication(DeliverySnapshot snapshot, ReshapeDescriptor descriptor)
    {
        if (snapshot.Target.Module == descriptor.Owner)
        {
            return;
        }

        var input = Event(descriptor.InputEvent);
        var output = Event(descriptor.OutputEvent);
        var target = _modules.Modules.SingleOrDefault(manifest => manifest.Id == snapshot.Target.Module)
            ?? throw new InvalidOperationException($"Delivery target module '{snapshot.Target.Module}' is not installed.");
        if (input.Visibility != EventVisibility.Published
            || output.Visibility != EventVisibility.Published
            || !target.ConsumedEvents.Contains(descriptor.OutputEvent))
        {
            throw new InvalidOperationException("A cross-module reshape requires published input and output events accepted by the target module.");
        }
    }

    private EventDescriptor Event(ContractId contract)
        => _modules.EventIndex.TryGetValue(contract.Value, out var @event)
            ? @event
            : throw new InvalidOperationException($"Event '{contract}' is not declared.");

    private interface IRegisteredReshape
    {
        ReshapeDescriptor Descriptor { get; }

        void ValidateSource(IDomainEvent source);

        IDomainEvent Transform(IDomainEvent source);
    }

    private sealed class RegisteredReshape<TFrom, TTo>(
        ReshapeDescriptor descriptor,
        IReshape<TFrom, TTo> reshape) : IRegisteredReshape
        where TFrom : IDomainEvent
        where TTo : IDomainEvent
    {
        public ReshapeDescriptor Descriptor { get; } = descriptor;

        public void ValidateSource(IDomainEvent source)
        {
            if (source is not TFrom)
            {
                throw new InvalidOperationException("The firing payload does not match the reshape input event type.");
            }
        }

        public IDomainEvent Transform(IDomainEvent source)
            => reshape.Transform((TFrom)source);
    }
}
