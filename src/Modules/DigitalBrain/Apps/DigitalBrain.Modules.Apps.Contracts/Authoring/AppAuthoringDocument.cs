namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.authoring-document")]
public sealed record AppAuthoringDocument(
    [property: Id(0)] int Version,
    [property: Id(1)] string Preamble,
    [property: Id(2)] AppBehaviorDocument[] Behaviors,
    [property: Id(3)] AppScenarioDocument[] Scenarios);

[GenerateSerializer, Alias("apps.behavior-document")]
public sealed record AppBehaviorDocument(
    [property: Id(0)] string Id,
    [property: Id(1)] string Title,
    [property: Id(2)] string Description,
    [property: Id(3)] string[] SourcePaths,
    [property: Id(4)] string[] ScenarioIds);

[GenerateSerializer, Alias("apps.scenario-document")]
public sealed record AppScenarioDocument(
    [property: Id(0)] string Id,
    [property: Id(1)] string Name,
    [property: Id(2)] string Body,
    [property: Id(3)] bool IsLive);

[GenerateSerializer, Alias("apps.document-read-result")]
public sealed record AppDocumentReadResult(
    [property: Id(0)] AppAuthoringDocument? Document,
    [property: Id(1)] string OriginalSpec,
    [property: Id(2)] bool CanEdit,
    [property: Id(3)] string? Error);
