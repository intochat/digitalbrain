using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Messaging;

namespace DigitalBrain.SmartPrompt;

[GenerateSerializer]
[Alias("db.smart-prompt.run.v1")]
public sealed record RunSmartPrompt(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string PromptName,
    [property: Id(2)] NeuronId? OfferChat) : Synapse;
