namespace DigitalBrain.Abstractions.Synapses;

[GenerateSerializer]
[Alias("db.synapse-kind")]
public enum SynapseKind
{
    // Declared by IHandle<T> at compile time. Never decays, never pruned, may block.
    Innate,

    // Created by a successful fire. Decays; pruned below the floor.
    Learned,

    // Created by tier-3 similarity search. Decays fastest to nothing; may never block.
    Discovered,
}
