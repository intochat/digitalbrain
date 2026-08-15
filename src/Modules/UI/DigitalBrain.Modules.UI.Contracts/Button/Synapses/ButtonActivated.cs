using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias(AliasName)]
public sealed record ButtonActivated(
    [property: Id(0)] CommandId OfferCommandId,
    [property: Id(1)] NeuronId Button,
    [property: Id(2)] string Action) : Synapse
{
    public const string AliasName = "ui.button-activated";
}
