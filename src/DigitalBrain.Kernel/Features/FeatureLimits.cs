using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Features;

internal static class FeatureLimits
{
    public const int InstallationsPerOwner = 100;
    public const int DraftsPerOwner = 100;
    public const int DraftGoalCharacters = 4096;
    public const int DraftOperationIdCharacters = 256;
    public const int DraftConversationIdCharacters = 256;
    public const int DraftScenarios = 32;
    public const int DraftScenarioIdCharacters = 128;
    public const int DraftScenarioNameCharacters = 256;
    public const int DraftScenarioStepCharacters = 4096;
    public const int DraftBehaviorUtf8Bytes = 65_536;
    public const int DraftSourceFiles = 64;
    public const int DraftSourceFileUtf8Bytes = 1_048_576;
    public const int DraftSourceUtf8Bytes = 4_194_304;
    public const int DraftSourcePathCharacters = 240;
    public const int DraftOwnerUtf8Bytes = 16 * 1024 * 1024;
    public const int DraftReplayRecords = 64;
    public const int DraftReplayUtf8Bytes = 10 * 1024 * 1024;
    public const int DraftVerificationScenarios = 1024;
    public const int DraftVerificationScenarioIdCharacters = 256;
    public const int DraftVerificationScenarioNameCharacters = 512;
    public const int DraftVerificationSafeFailureCharacters = 4096;
    public const int DraftVerificationDurationMilliseconds = 70_000;
    public const int DraftVerificationEvidenceUtf8Bytes = 2 * 1024 * 1024;
    public const int DraftVerificationArtifacts = 32;
    public const int DraftVerificationArtifactNameCharacters = 256;
    public const int DraftVerificationArtifactMediaTypeCharacters = 128;
    public const int DraftVerificationArtifactBytes = 1_048_576;
    public const int DraftPatchIdCharacters = 256;
    public const int DraftPatchSummaryCharacters = 2048;
    public const int DraftSuggestionGuidanceCharacters = 4096;
    public const int DraftSuggestionPayloadUtf8Bytes = 5 * 1024 * 1024;
    public const int DraftInstallationReservations = 100;
    public const int DraftInstallationLedgerUtf8Bytes = 8 * 1024 * 1024;
    public const int ApprovalLedgerRecords = 64;
    public const int ApprovalLedgerUtf8Bytes = 4 * 1024 * 1024;
    public const int InboxEntries = 1_000;
    public const int ScheduleCursors = 1_000;
    public const int StateUtf8Bytes = 64 * 1024;
    public const int IntentsPerRun = 32;
    public const int EffectResolutionsPerRun = 32;
    public const int IntentOperationKeyUtf8Bytes = 1024;
    public const int EffectDecisionIdUtf8Bytes = 768;
    public const int EffectActorScopeUtf8Bytes = 64;
    public const int EffectSafeResultUtf8Bytes = 1536;
    public const int EffectResolutionHistoryUtf8Bytes = EffectResolutionsPerRun *
        (IntentOperationKeyUtf8Bytes + EffectDecisionIdUtf8Bytes + EffectActorScopeUtf8Bytes + EffectSafeResultUtf8Bytes);
    public const int ReadsPerRun = 20;
    public const int ModelCallsPerRun = 4;
    public const int AttemptsPerInput = 5;
    public const int FanOutBatches = 1_000;
    public const int IntentLedgerEntries = 256;
    public const int IntentLedgerUtf8Bytes = 4 * 1024 * 1024;
    public static readonly TimeSpan RunDeadline = TimeSpan.FromSeconds(60);
}
internal sealed class FeatureLimitExceededException(string message) : InvalidOperationException(message);
internal sealed class FeatureConcurrencyException(
    string message,
    FeatureCommandRejectionReason reason = FeatureCommandRejectionReason.Conflict) : InvalidOperationException(message)
{
    public FeatureCommandRejectionReason Reason { get; } = reason;
}
