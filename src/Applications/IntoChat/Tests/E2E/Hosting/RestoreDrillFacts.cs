using System.Diagnostics;
using IntoChat.Operations;

namespace IntoChat.Tests.E2E.Hosting;

// Harness-gated restore drill (C17). A real drill needs a nightly snapshot and a fresh stack, so it
// only runs when the harness opts in. It stands up a fresh product stack, proves it is healthy,
// validates the snapshot through ops/backup/restore.sh --dry-run, and confirms the stack is still
// healthy afterwards.
public sealed class RestoreDrillFacts
{
    public static bool RestoreDrillEnabled => Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_RESTORE_DRILL") == "1";

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact(Timeout = 300_000, SkipUnless = nameof(RestoreDrillEnabled), Skip = "Set DIGITALBRAIN_E2E_RESTORE_DRILL=1 to run the hosted restore drill.")]
    [Trait("Category", "Hosting")]
    public async Task RestoresASnapshotIntoAFreshStack()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);
        using (var health = await brain.HttpClient.GetAsync("/health", ct))
        {
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        }

        Assert.Equal(3, BackupPlan.Targets.Count);
        var snapshot = CreateSnapshotDirectory();
        try
        {
            var exitCode = await RunRestoreDryRunAsync(snapshot, ct);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            Directory.Delete(snapshot, recursive: true);
        }

        using var after = await brain.HttpClient.GetAsync("/health", ct);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    private static string CreateSnapshotDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "intochat-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "grain-state.txt"), "digitalbrain-v2-state/workspace-example.json");
        File.WriteAllBytes(Path.Combine(directory, "ledger.dump"), [0x50, 0x47, 0x44, 0x4D, 0x50]);
        File.WriteAllBytes(Path.Combine(directory, "qdrant.snapshot"), [0x51, 0x44, 0x52, 0x41, 0x4E, 0x54]);
        return directory;
    }

    private static async Task<int> RunRestoreDryRunAsync(string snapshot, CancellationToken ct)
    {
        var script = Path.Combine(RepositoryRoot, "ops", "backup", "restore.sh");
        var startInfo = new ProcessStartInfo("bash", $"\"{script}\" --snapshot \"{snapshot}\" --dry-run")
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("bash could not be started.");
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        Assert.True(process.ExitCode == 0, $"restore.sh dry-run failed ({process.ExitCode}): {output}\n{error}");
        return process.ExitCode;
    }

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
}
