using DigitalBrain.Apps;

namespace IntoChat.Marketplace;

[GenerateSerializer, Alias("intochat.app-draft-state")]
public sealed record AppDraftState
{
    [Id(0)] public long Revision { get; init; }
    [Id(1)] public string Request { get; init; } = "";
    [Id(2)] public string Name { get; init; } = "";
    [Id(3)] public string Title { get; init; } = "";
    [Id(4)] public string Description { get; init; } = "";
    [Id(5)] public string Runtime { get; init; } = "";
    [Id(6)] public string Spec { get; init; } = "";
    [Id(7)] public AppDraftStatus Status { get; init; }
    [Id(8)] public IReadOnlyList<AppDraftAttempt> Attempts { get; init; } = [];
    [Id(9)] public PackageRevisionRef? Published { get; init; }
    [Id(10)] public string Error { get; init; } = "";
}
