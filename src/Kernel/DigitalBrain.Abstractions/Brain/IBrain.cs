namespace DigitalBrain.Abstractions.Brain;

// [ClientEntryPoint]: the IDigitalBrain facade is an unattributed external client, which the
// OwnerBoundCallFilter admits by this attribute alone — owner scoping comes from the CALLER'S
// key construction under the standing trusted-zone posture (pinned in TestEntities.cs).
// Register, Connect/Disconnect, and UseContext are writes riding the same gate.
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
