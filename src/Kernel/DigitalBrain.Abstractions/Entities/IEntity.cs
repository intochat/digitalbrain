namespace DigitalBrain.Abstractions.Entities;

// An entity is a plain stateful grain: direct-call read/write, no journals, no signal
// membrane, never a graph endpoint. Neurons drive entity writes and journal the effect.
[Alias("db.entity")]
public interface IEntity : IGrainWithStringKey
{
}

// Reads are the client-facing query surface (JOURNALS.md rule 3); writes stay behind each concrete contract's own entry-point opt-in.
[Alias("db.entity-state")]
public interface IEntity<TState> : IEntity
    where TState : class
{
    [Alias(nameof(Read))]
    Task<TState?> Read();
}
