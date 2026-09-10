using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Google;

/// <summary>Confirms the stored preview and provider schema.</summary>
[GenerateSerializer]
[Alias("db.gmail.confirm-draft")]
public sealed record ConfirmGmailDraft(
    CommandId Id,
    [property: Id(0)] string PreviewId,
    [property: Id(1)] string ToolSchemaHash) : Command(Id);
