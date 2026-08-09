namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.request-synapse")]
public abstract record RequestSynapse<TResponse> : Synapse
    where TResponse : Synapse;
