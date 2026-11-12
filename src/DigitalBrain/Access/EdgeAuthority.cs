#pragma warning disable CA1822 // Mark members as static

using DigitalBrain.Modules;

namespace DigitalBrain.Access;

/// <summary>
/// Edge minting authority for callers. Handed to trusted edge adapters and host initialization.
/// Has no public constructor; only authorized hosts can instantiate it.
/// </summary>
public sealed class EdgeAuthority
{
    private readonly ICallerCarrier _carrier;

    internal EdgeAuthority(ICallerCarrier carrier)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        _carrier = carrier;
    }

    internal static EdgeAuthority CreateInternal(ICallerCarrier? carrier = null) =>
        new(carrier ?? NullCarrier.Instance);

    private sealed class NullCarrier : ICallerCarrier
    {
        public static readonly NullCarrier Instance = new();
        public Caller? Current => null;
        public IDisposable Enter(Caller? caller) => NoOpDisposable.Instance;
        private sealed class NoOpDisposable : IDisposable { public static readonly NoOpDisposable Instance = new(); public void Dispose() { } }
    }

    /// <summary>Mints a human person caller.</summary>
    public Caller Person(string principal, string account, BrainId brain, string? conversation = null)
        => new(new Actor.Person(principal, account), brain, conversation);

    /// <summary>Mints an agent caller acting on behalf of a human person.</summary>
    public Caller Agent(Actor.Person forPerson, BrainId brain, string? conversation = null)
        => new(new Actor.Agent(forPerson), brain, conversation);

    /// <summary>Mints an application caller acting on behalf of a human person.</summary>
    public Caller App(string key, Actor.Person forPerson, BrainId brain, string? conversation = null)
        => new(new Actor.App(key, forPerson), brain, conversation);

    /// <summary>Mints an internal module caller.</summary>
    public Caller Module(string id, BrainId brain)
        => new(new Actor.Module(id), brain, inside: true);

    /// <summary>Mints a scheduler caller.</summary>
    public Caller Scheduler(BrainId brain)
        => new(new Actor.Scheduler(), brain, inside: true);

    /// <summary>Mints a kernel caller.</summary>
    public Caller Kernel(BrainId brain)
        => Caller.OfKernel(brain);

    /// <summary>Enters the ambient execution frame of <paramref name="caller"/>.</summary>
    public IDisposable Enter(Caller caller) => _carrier.Enter(caller);

    /// <summary>Enters a kernel execution frame for the given <paramref name="brain"/>.</summary>
    public IDisposable EnterKernel(BrainId brain) => _carrier.Enter(Caller.OfKernel(brain));
}
#pragma warning restore CA1822
