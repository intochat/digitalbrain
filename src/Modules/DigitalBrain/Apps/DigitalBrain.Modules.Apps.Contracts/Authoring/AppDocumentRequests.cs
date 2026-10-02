namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.save-document")]
public sealed record SaveAppDocument(
    [property: Id(0)] long ExpectedRevision,
    [property: Id(1)] AppAuthoringDocument Document);

[GenerateSerializer, Alias("apps.draft-conflict")]
public sealed class AppDraftConflictException(string message) : Exception(message);

public sealed record ImportAppDocument(PackageRevisionRef Revision, long ExpectedRevision);
