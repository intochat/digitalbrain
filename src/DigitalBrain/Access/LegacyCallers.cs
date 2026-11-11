using DigitalBrain.Modules;
using DigitalBrain.Capabilities;

namespace DigitalBrain.Access;

/// <summary>
/// Release 1 adapter between unforgeable <see cref="Caller"/> and legacy <see cref="CapabilityCaller"/>.
/// </summary>
internal static class LegacyCallers
{
    public static Caller Read(CapabilityCaller legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        var brain = new BrainId(legacy.BrainId);
        Actor actor = legacy.Kind switch
        {
            CallerKind.Platform => new Actor.Kernel(),
            CallerKind.Scheduler => new Actor.Scheduler(),
            CallerKind.Agent => new Actor.Agent(new Actor.Person(legacy.PrincipalId, legacy.AccountId)),
            CallerKind.App => new Actor.App(legacy.AppId ?? string.Empty, new Actor.Person(legacy.PrincipalId, legacy.AccountId)),
            _ => new Actor.Person(legacy.PrincipalId, legacy.AccountId)
        };
        return new Caller(actor, brain, legacy.ConversationId);
    }

    public static CapabilityCaller? Project(Caller? caller)
    {
        if (caller is null) return null;
        var brain = caller.Brain.Value;
        return caller.Actor switch
        {
            Actor.Person p => new CapabilityCaller(p.Principal, p.Account, brain, ConversationId: caller.Conversation),
            Actor.Agent ag => new CapabilityCaller(ag.For.Principal, ag.For.Account, brain, CapabilityCallers.AssistantApp, caller.Conversation),
            Actor.App ap => new CapabilityCaller(ap.For.Principal, ap.For.Account, brain, ap.Key, caller.Conversation),
            Actor.Module m => new CapabilityCaller(m.Id, m.Id, brain, m.Id, caller.Conversation),
            Actor.Scheduler => new CapabilityCaller(brain, brain, brain, CapabilityCallers.SchedulerApp, caller.Conversation),
            Actor.Kernel => new CapabilityCaller(brain, brain, brain, CapabilityCallers.PlatformApp, caller.Conversation),
            _ => new CapabilityCaller(brain, brain, brain, ConversationId: caller.Conversation)
        };
    }
}
