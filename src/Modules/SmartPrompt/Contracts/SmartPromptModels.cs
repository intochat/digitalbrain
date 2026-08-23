namespace DigitalBrain.SmartPrompt;

[GenerateSerializer]
[Alias("db.smart-prompt.binding.v1")]
public sealed record SmartPromptBinding(
    [property: Id(0)] string Kind,
    [property: Id(1)] string? Label,
    [property: Id(2)] string? Account);

[GenerateSerializer]
[Alias("db.smart-prompt.document.v1")]
public sealed record SmartPromptDocument(
    [property: Id(0)] string Title,
    [property: Id(1)] string BodyText,
    [property: Id(2)] IReadOnlyList<SmartPromptBinding> Bindings,
    [property: Id(3)] bool Enabled);

[GenerateSerializer]
[Alias("db.smart-prompt.state.v1")]
public sealed record SmartPromptState(
    [property: Id(0)] SmartPromptDocument Document,
    [property: Id(1)] Guid? ActiveRevisionId);
