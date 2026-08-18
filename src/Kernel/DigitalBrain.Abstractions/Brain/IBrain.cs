namespace DigitalBrain.Abstractions.Brain;

// [ClientEntryPoint] on the whole contract: the IDigitalBrain facade reaches this grain as an
// unattributed external Orleans client (OwnerBoundCallFilter attributes only grain-to-grain
// calls), so UseContext/Contexts/Resolve must be entry-point-visible for the facade to reach
// them at all. Register rides along visibly but harmlessly: registrations are owner-scoped
// snapshot upserts and idempotent.
[ClientEntryPoint]
[Alias("db.brain")]
public interface IBrain : IEntity<BrainState>
{
    [Alias(nameof(Register))]
    Task Register(BrainReference reference);

    [Alias(nameof(Resolve))]
    Task<BrainReference?> Resolve(string hint, string? context = null);

    [Alias(nameof(UseContext))]
    Task UseContext(string name);

    [Alias(nameof(Contexts))]
    Task<IReadOnlyList<BrainContext>> Contexts();

    [Alias(nameof(Connect))]
    Task Connect(Connection connection);

    [Alias(nameof(Disconnect))]
    Task Disconnect(Connection connection);

    [Alias(nameof(Route))]
    Task<Connection?> Route(string alias);
}
