
namespace DigitalBrain.Access;

/// <summary>
/// The caller of the current call. No public constructor, no init accessor, not serializable.
/// </summary>
public sealed record Caller
{
    internal Caller(Actor actor, BrainId brain, string? conversation = null, NeuronId? via = null, bool inside = false)
    {
        ArgumentNullException.ThrowIfNull(actor);
        Actor = actor;
        Brain = brain;
        Conversation = conversation;
        Via = via;
        Inside = inside;
    }

    /// <summary>Who is behind this call.</summary>
    public Actor Actor { get; }

    /// <summary>The brain the caller acts in, decided at the edge after the membership check.</summary>
    public BrainId Brain { get; }

    /// <summary>The model conversation the call runs for.</summary>
    public string? Conversation { get; }

    /// <summary>The neuron whose proxy made this call.</summary>
    public NeuronId? Via { get; }

    /// <summary>Whether the sender is a grain of this cluster.</summary>
    public bool Inside { get; }

    /// <summary>A human directly: not routed through an intermediary neuron and not sent by a cluster grain.</summary>
    public bool Direct => Via is null && !Inside;

    /// <summary>The human person on whose behalf the call runs, if any.</summary>
    public Actor.Person? For => Actor switch
    {
        Actor.Person person => person,
        Actor.Agent agent => agent.For,
        Actor.App app => app.For,
        _ => null
    };

    internal static Caller OfKernel(BrainId brain, NeuronId? via = null) => new(new Actor.Kernel(), brain, null, via, inside: true);

    internal Caller ActingIn(BrainId brain) => new(Actor, brain, Conversation, Via, Inside);

    internal Caller Through(string appKey)
        => Actor is Actor.Person or Actor.App ? new(new Actor.App(appKey, For!), Brain, Conversation, Via, Inside) : this;

    internal Caller Hop(NeuronId? via, bool inside) => new(Actor, Brain, Conversation, via, inside);
}
