#pragma warning disable CA1822 // Mark members as static

using DigitalBrain.Capabilities;
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
        public bool IsKernelActive => false;
        public IDisposable Enter(Caller? caller) => NoOpDisposable.Instance;
        public IDisposable EnterKernelScope(bool clearCaller) => NoOpDisposable.Instance;
        private sealed class NoOpDisposable : IDisposable { public static readonly NoOpDisposable Instance = new(); public void Dispose() { } }
    }

    /// <summary>The currently active caller on this authority's carrier, or null when none is established.</summary>
    public Caller? Current => _carrier.Current;

    /// <summary>True when an authorized in-process kernel scope or kernel caller is active on this carrier.</summary>
    public bool IsKernelActive => _carrier.IsKernelActive;

    /// <summary>Mints a human person caller.</summary>
    public Caller Person(string principal, string account, BrainId brain, string? conversation = null)
        => new(new Actor.Person(principal, account), brain, conversation);

    /// <summary>Mints an agent caller acting on behalf of a human person.</summary>
    public Caller Agent(Actor.Person forPerson, BrainId brain, string? conversation = null)
        => new(new Actor.Agent(forPerson), brain, conversation);

    /// <summary>Mints an application caller acting on behalf of a human person.</summary>
    public Caller App(string key, Actor.Person forPerson, BrainId brain, string? conversation = null)
        => new(new Actor.App(key, forPerson), brain, conversation);

    /// <summary>Mints a restricted guest caller without direct human person authority.</summary>
    public Caller Guest(string principal, string account, BrainId brain, string? conversation = null)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(account);
        return new(new Actor.App("guest", new Actor.Person(principal, account)), brain, conversation);
    }

    /// <summary>Mints a relayed human person caller without direct edge authority.</summary>
    public Caller RelayedPerson(string principal, string account, BrainId brain, NeuronId? via = null, string? conversation = null, bool inside = true)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(account);
        if (via is null && !inside)
        {
            throw new ArgumentException("A relayed person must specify an intermediary neuron or inside=true so Direct remains false.", nameof(via));
        }
        return new(new Actor.Person(principal, account), brain, conversation, via, inside);
    }

    /// <summary>Mints a delegated caller acting on behalf of <paramref name="forPerson"/> without direct human edge authority.</summary>
    public Caller OnBehalfOf(Actor.Person forPerson, BrainId brain, string? viaApp = null, NeuronId? via = null, string? conversation = null)
    {
        ArgumentNullException.ThrowIfNull(forPerson);
        return string.IsNullOrWhiteSpace(viaApp)
            ? new(forPerson, brain, conversation, via, inside: true)
            : new(new Actor.App(viaApp, forPerson), brain, conversation, via, inside: via is not null);
    }

    /// <summary>Mints an internal module caller.</summary>
    public Caller Module(string id, BrainId brain)
        => new(new Actor.Module(id), brain, inside: true);

    /// <summary>Mints a scheduler caller.</summary>
    public Caller Scheduler(BrainId brain)
        => new(new Actor.Scheduler(), brain, inside: true);

    /// <summary>Mints a kernel caller.</summary>
    public Caller Kernel(BrainId brain)
        => Caller.OfKernel(brain);

    /// <summary>Converts a verified host <see cref="CapabilityCaller"/> stamp into a <see cref="Caller"/>.</summary>
    public Caller From(CapabilityCaller caller) => LegacyCallers.Read(caller);

    /// <summary>Enters the ambient execution frame of <paramref name="caller"/>.</summary>
    public IDisposable Enter(Caller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        return _carrier.Enter(caller);
    }

    /// <summary>Enters a kernel execution frame for the given <paramref name="brain"/>.</summary>
    public IDisposable EnterKernel(BrainId brain) => _carrier.Enter(Caller.OfKernel(brain));

    /// <summary>Enters an authorized in-process host scope without altering the ambient caller.</summary>
    public IDisposable EnterInProcess() => _carrier.EnterKernelScope(clearCaller: false);

    /// <summary>Clears any inherited caller and enters an authorized unstamped in-process kernel scope.</summary>
    public IDisposable ClearCaller() => _carrier.EnterKernelScope(clearCaller: true);
}
#pragma warning restore CA1822
