namespace DigitalBrain.Apps;

// What the marketplace shows about an app beyond its listing: its spec and tests as the author wrote
// them, with the verdicts of the revision's last verification.
public sealed record AppSpecView(
    PackageRevisionRef Revision,
    string Runtime,
    IReadOnlyDictionary<string, string> Files,
    string? Spec,
    string? Tests,
    AppVerification? Verification,
    PackageManifest? Manifest = null,
    AppDocumentReadResult? DocumentReadResult = null,
    SpecToken[]? Vocabulary = null,
    bool CanEdit = false);


