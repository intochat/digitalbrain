using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Chat;

/// <summary>Sends text with optional context to start a chat turn.</summary>
[GenerateSerializer]
[Alias("chat.send-message")]
public sealed record SendMessage(
    CommandId Id,
    [property: Id(0)] string Text,
    [property: Id(1)] IReadOnlyList<ContextRef>? Context = null) : Command(Id);
