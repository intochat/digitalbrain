using IntoChat.Operations;

namespace IntoChat.Tests.Operations;

public sealed class BackupPlanFacts
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException($"Repository root (DigitalBrain.slnx) was not found above {AppContext.BaseDirectory}.");
    }

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void NightlyPlanCoversGrainStorageLedgerAndVectors()
    {
        Assert.Equal(3, BackupPlan.Targets.Count);
        Assert.Equal(
            [BackupTargetKind.GrainStorage, BackupTargetKind.Ledger, BackupTargetKind.VectorStore],
            BackupPlan.Targets.Select(target => target.Kind).ToArray());
        foreach (var target in BackupPlan.Targets)
        {
            Assert.Equal(BackupPlan.NightlySchedule, target.Schedule);
            Assert.True(target.RetentionDays > 0, $"{target.Name} must state a retention window.");
            Assert.False(string.IsNullOrWhiteSpace(target.RestoreCommand), $"{target.Name} must state a restore command.");
        }
    }

    [Fact]
    public void RestoreScriptAndCronJobsCoverEveryTarget()
    {
        var restore = Read("ops/backup/restore.sh");
        var jobs = Read("ops/backup/nightly-backup.yaml");
        foreach (var target in BackupPlan.Targets)
        {
            Assert.Contains(target.Name, restore);
            Assert.Contains(target.Name, jobs);
        }
        Assert.Contains("--dry-run", restore);
        Assert.Contains(BackupPlan.NightlySchedule, jobs);
    }
}
