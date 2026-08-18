namespace DigitalBrain.Abstractions.Entities;

// An entity is a plain stateful grain: direct-call read/write, no journals, no synapse
// membrane, never a graph endpoint. Neurons drive entity writes and journal the effect.
[Alias("db.entity")]
public interface IEntity : IGrainWithStringKey
{
}

[Alias("db.entity-state")]
public interface IEntity<TState> : IEntity
    where TState : class
{
    [Alias(nameof(Read))]
    Task<TState?> Read();
}
