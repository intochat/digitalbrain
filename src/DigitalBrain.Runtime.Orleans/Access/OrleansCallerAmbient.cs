using DigitalBrain.Modules;
using DigitalBrain.Access;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Access;

internal sealed class OrleansCallerAmbient : ICallerCarrier
{
    internal const string Key = "db.access.caller";

    public static OrleansCallerAmbient Instance { get; } = new();

    public Caller? Current => RequestContext.Get(Key) is CallerWire wire ? Read(wire) : null;

    public IDisposable Enter(Caller? caller)
    {
        var previous = RequestContext.Get(Key);
        Set(caller is null ? null : Write(caller));
        return new Restore(previous);
    }

    internal static Caller? Read(CallerWire? wire)
    {
        if (wire is null) return null;
        var brain = new BrainId(wire.Brain);
        Actor actor = wire.Actor switch
        {
            0 => new Actor.Person(wire.Principal ?? string.Empty, wire.Account ?? string.Empty),
            1 => new Actor.Agent(new Actor.Person(wire.Principal ?? string.Empty, wire.Account ?? string.Empty)),
            2 => new Actor.App(wire.Name ?? string.Empty, new Actor.Person(wire.Principal ?? string.Empty, wire.Account ?? string.Empty)),
            3 => new Actor.Module(wire.Name ?? string.Empty),
            4 => new Actor.Scheduler(),
            5 => new Actor.Kernel(),
            _ => new Actor.Person(wire.Principal ?? string.Empty, wire.Account ?? string.Empty)
        };

        NeuronId? via = string.IsNullOrEmpty(wire.Via) ? null : NeuronId.Parse(wire.Via);
        return new Caller(actor, brain, wire.Conversation, via, wire.Inside);
    }

    internal static CallerWire Write(Caller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var wire = new CallerWire
        {
            Brain = caller.Brain.Value,
            Conversation = caller.Conversation,
            Via = caller.Via?.ToString(),
            Inside = caller.Inside
        };

        switch (caller.Actor)
        {
            case Actor.Person person:
                wire.Actor = 0;
                wire.Principal = person.Principal;
                wire.Account = person.Account;
                break;
            case Actor.Agent agent:
                wire.Actor = 1;
                wire.Principal = agent.For.Principal;
                wire.Account = agent.For.Account;
                break;
            case Actor.App app:
                wire.Actor = 2;
                wire.Name = app.Key;
                wire.Principal = app.For.Principal;
                wire.Account = app.For.Account;
                break;
            case Actor.Module module:
                wire.Actor = 3;
                wire.Name = module.Id;
                break;
            case Actor.Scheduler:
                wire.Actor = 4;
                break;
            case Actor.Kernel:
                wire.Actor = 5;
                break;
            default:
                wire.Actor = 255;
                break;
        }

        return wire;
    }

    private static void Set(CallerWire? wire)
    {
        if (wire is null) { RequestContext.Remove(Key); }
        else { RequestContext.Set(Key, wire); }
    }

    private sealed class Restore(object? previous) : IDisposable
    {
        public void Dispose()
        {
            if (previous is null) { RequestContext.Remove(Key); }
            else { RequestContext.Set(Key, previous); }
        }
    }
}
