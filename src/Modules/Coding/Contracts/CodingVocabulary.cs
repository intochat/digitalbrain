namespace DigitalBrain.Coding;

public static class CodingVocabulary
{
    public const string WorkspaceType = "workspace";
    public const string ChangeSetType = "changeset";

    // ---- work a command schedules for its own reaction ----
    public const string WorkspaceOpening = "WorkspaceOpening";
    public const string WorkspaceReloading = "WorkspaceReloading";
    public const string WorkspaceMapping = "WorkspaceMapping";
    public const string ChangeSetProposing = "ChangeSetProposing";
    public const string ChangeSetChecking = "ChangeSetChecking";
    public const string ChangeSetCommitting = "ChangeSetCommitting";
    public const string ChangeSetDiscarding = "ChangeSetDiscarding";
}
