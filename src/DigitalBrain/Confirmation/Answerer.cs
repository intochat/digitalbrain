using DigitalBrain.Access;

namespace DigitalBrain.Confirmation;

/// <summary>Who is answering a confirmation, and whether they hold direct human authority.</summary>
public sealed record Answerer
{
    private Answerer(ConfirmationPrincipal principal, bool isPerson)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (isPerson && !principal.IsHuman)
        {
            throw new ArgumentException("Only a human principal may answer as a person.", nameof(isPerson));
        }

        Principal = principal;
        IsPerson = isPerson;
    }

    /// <summary>The principal recorded on the confirmation outcome and audit signal.</summary>
    public ConfirmationPrincipal Principal { get; }

    /// <summary>True only when the caller is a direct human person outside any conversation.</summary>
    public bool IsPerson { get; }

    /// <summary>Derives an <see cref="Answerer"/> from an ambient <see cref="Caller"/> in <paramref name="brain"/>.</summary>
    public static Answerer Of(Caller? caller, BrainId brain)
    {
        if (caller is not null && caller.Brain != brain)
        {
            throw new UnauthorizedAccessException("This confirmation belongs to another brain.");
        }

        var isPerson = caller is { Direct: true, Conversation: null, Actor: Actor.Person };
        return new Answerer(PrincipalOf(caller), isPerson);
    }

    /// <summary>Creates a direct human person answerer.</summary>
    public static Answerer Person(string id) => new(ConfirmationPrincipal.Person(id), isPerson: true);

    /// <summary>Creates a relayed or conversation-scoped person answerer without direct person authority.</summary>
    public static Answerer RelayedPerson(string id) => new(ConfirmationPrincipal.Person(id), isPerson: false);

    /// <summary>Creates an app answerer.</summary>
    public static Answerer App(string id) => new(ConfirmationPrincipal.App(id), isPerson: false);

    /// <summary>Creates an agent answerer.</summary>
    public static Answerer Agent(string id) => new(ConfirmationPrincipal.Agent(id), isPerson: false);

    /// <summary>Creates an answerer from an explicit <see cref="ConfirmationPrincipal"/>.</summary>
    public static Answerer From(ConfirmationPrincipal principal, bool direct = false)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return new Answerer(principal, isPerson: direct && principal.IsHuman);
    }

    internal static ConfirmationPrincipal PrincipalOf(Caller? caller) => caller?.Actor switch
    {
        Actor.Person p => ConfirmationPrincipal.Person(p.Principal),
        Actor.App a => ConfirmationPrincipal.App(string.IsNullOrEmpty(a.Key) ? a.For.Principal : a.Key),
        Actor.Agent ag => ConfirmationPrincipal.Agent(ag.For.Principal),
        _ => ConfirmationPrincipal.Agent("unknown"),
    };
}
