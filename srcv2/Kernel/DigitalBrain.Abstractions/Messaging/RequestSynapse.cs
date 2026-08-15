namespace DigitalBrain.Abstractions.Messaging;

[GenerateSerializer]
[Alias("db.request-synapse")]
public abstract record RequestSynapse<TResponse> : Synapse
    where TResponse : Synapse;
