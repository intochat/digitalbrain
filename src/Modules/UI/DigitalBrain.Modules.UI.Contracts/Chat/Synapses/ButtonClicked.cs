using DigitalBrain.Abstractions;

namespace DigitalBrain.UI;

// Frozen shell wire: the shell's chat.button owner command and the MCP activate_chat_button
// tool both land this click in the conversation's journal.
[GenerateSerializer]
[Alias("ui.button-clicked")]
public sealed record ButtonClicked(
    [property: Id(0)] CommandId OfferCommandId,
    [property: Id(1)] string ButtonId,
    [property: Id(2)] string Action) : Synapse;
