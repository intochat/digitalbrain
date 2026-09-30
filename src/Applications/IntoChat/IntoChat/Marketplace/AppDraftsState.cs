namespace IntoChat.Marketplace;

[GenerateSerializer, Alias("intochat.app-drafts-state")]
public sealed record AppDraftsState
{
    [Id(0)] public AppDraftEntry[] Entries { get; init; } = [];
}
