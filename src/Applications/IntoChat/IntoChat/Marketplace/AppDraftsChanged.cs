using DigitalBrain.Contracts;

namespace IntoChat.Marketplace;

[GenerateSerializer, Alias("intochat.app-drafts-changed")]
public sealed record AppDraftsChanged([property: Id(0)] string Owner) : Signal;
