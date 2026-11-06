using DigitalBrain.Brains;
using DigitalBrain.Capabilities;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Signals;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Runtime.Orleans.Brain;

/// <summary>
/// The brain's first neuron: its state, its creation and its lifecycle signals, a plain neuron
/// keyed {brain}/main. It also publishes the kernel's instance and type signals, so they arrive with the brain as publisher.
/// </summary>
[DigitalBrain.Neuron(IBrain.Type, State = "brain")]
public sealed class BrainNeuron
    : DigitalBrain.Neuron<BrainStorage>, IBrain, IBrainAnnouncer
{
    private readonly IEnumerable<ICreationHandler> creation;
    private readonly ILogger<BrainNeuron> logger;

    private string BrainId => Id.Brain.Value;

    private static readonly Action<ILogger, string, string, Exception?> CreationHandlerFailed =
        LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(1, nameof(CreationHandlerFailed)), "Creation handler {Handler} failed for brain {Brain}.");

    public BrainNeuron(IEnumerable<ICreationHandler> creation, ILogger<BrainNeuron> logger)
    {
        this.creation = creation;
        this.logger = logger;
    }

    public Task<BrainState> Read()
        => State.Owner is { } owner ? Task.FromResult(new BrainState(owner, State.CreatedAt))
            : throw new InvalidOperationException("This brain has not been created.");

    public async Task Create(Owner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        RequireCreator(owner);
        if (State.Owner is not null) { return; }
        var created = new Created(owner);
        await Save(new BrainStorage { Owner = owner, CreatedAt = DateTimeOffset.UtcNow }, created);
        foreach (var handler in creation)
        {
            // One handler failing must not fail the brain; Activated handlers catch up later.
            try { await handler.Created(BrainId, created, CancellationToken.None); }
            catch (Exception error) when (error is not OperationCanceledException)
            { CreationHandlerFailed(logger, handler.GetType().Name, BrainId, error); }
        }
        await Publish(new Activated(owner));
    }

    // Every wake of a created brain says so; a brain that does not exist yet stays silent.
    protected override Task OnActivate(CancellationToken cancellationToken)
        => State.Owner is { } owner ? Publish(new Activated(owner)) : Task.CompletedTask;

    // The kernel's own grains announce; a stamped caller is someone else and is refused.
    public Task InstanceCreated(string type, string key) => Announce(new InstanceCreated(type, key));

    public Task InstanceRetired(string type, string key) => Announce(new InstanceRetired(type, key));

    public Task TypeAdded(string name) => Announce(new TypeAdded(name));

    private Task Announce(DigitalBrain.Signal signal)
    {
        if (RuntimeCallerContext.TryGet(out _)) { throw new UnauthorizedAccessException("Only the kernel announces a brain's instances and types."); }
        return Publish(signal);
    }

    // Creating is the platform's act, or the owner's own. Host code with no stamped caller is trusted.
    private static void RequireCreator(Owner owner)
    {
        if (!RuntimeCallerContext.TryGet(out var caller)) { return; }
        if (caller.Kind == CallerKind.Platform || caller.AccountId == owner.AccountId) { return; }
        throw new UnauthorizedAccessException("Only the platform or the owner may create a brain.");
    }
}
