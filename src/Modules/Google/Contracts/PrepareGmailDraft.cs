using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Google;

/// <summary>Requests an exact draft preview.</summary>
[GenerateSerializer]
[Alias("db.gmail.prepare-draft")]
public sealed record PrepareGmailDraft(
    CommandId Id,
    [property: Id(0)] IReadOnlyList<string> To,
    [property: Id(1)] IReadOnlyList<string> Cc,
    [property: Id(2)] IReadOnlyList<string> Bcc,
    [property: Id(3)] string Subject,
    [property: Id(4)] string Body) : Command(Id);
