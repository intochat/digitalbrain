namespace IntoChat.Operations;

internal enum BackupTargetKind
{
    GrainStorage,
    Ledger,
    VectorStore,
}

// The nightly backup set from the C17 operations plan: grain storage, the Compute ledger and the
// Qdrant vector collections. RPO 24h, RTO 4h. Restore commands are executed by ops/backup/restore.sh.
internal sealed record BackupTarget(
    string Name,
    BackupTargetKind Kind,
    string Schedule,
    int RetentionDays,
    string RestoreCommand);

internal static class BackupPlan
{
    public const string NightlySchedule = "0 2 * * *";

    public static IReadOnlyList<BackupTarget> Targets { get; } =
    [
        new(
            "grain-storage",
            BackupTargetKind.GrainStorage,
            NightlySchedule,
            30,
            "az storage blob copy start-batch --source-container digitalbrain-v2-state --destination-container digitalbrain-v2-state-restore"),
        new(
            "ledger",
            BackupTargetKind.Ledger,
            NightlySchedule,
            90,
            "pg_restore --clean --if-exists --dbname \"$LEDGER_DATABASE_URL\" \"$SNAPSHOT/ledger.dump\""),
        new(
            "qdrant",
            BackupTargetKind.VectorStore,
            NightlySchedule,
            30,
            "curl -fsS -X POST \"$QDRANT_URL/collections/$QDRANT_COLLECTION/snapshots/upload\" --form snapshot=@\"$SNAPSHOT/qdrant.snapshot\""),
    ];
}
