
namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("intochat.app-draft-view")]
public sealed record AppDraftView(
    [property: Id(0)] AppDraftState Draft,
    [property: Id(1)] AppVerification? Verification,
    [property: Id(2)] SpecToken[]? Vocabulary = null,
    [property: Id(3)] IReadOnlyDictionary<string, string>? Files = null,
    [property: Id(4)] PackageRevisionRef? FilesRevision = null,
    [property: Id(5)] bool VerificationCurrent = false);

