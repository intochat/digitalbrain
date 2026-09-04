using DigitalBrain.Scripting.Startup;
using Xunit;

namespace DigitalBrain.Scripting.Tests;

public sealed class FileStartupExecutionLedgerTests
{
    [Fact]
    public async Task Recorded_execution_is_visible_to_a_new_ledger_instance()
    {
        var directory = Directory.CreateTempSubdirectory("digitalbrain-scripting-");
        try
        {
            var key = new StartupExecutionKey("owner", "signal", "sha256");
            var execution = StartupExecution.Succeeded(key, "started", DateTimeOffset.UnixEpoch);

            await new FileStartupExecutionLedger(directory.FullName)
                .RecordAsync(execution, TestContext.Current.CancellationToken);

            var restored = await new FileStartupExecutionLedger(directory.FullName)
                .FindAsync(key, TestContext.Current.CancellationToken);

            Assert.Equal(execution, restored);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Recording_the_same_key_twice_keeps_the_first_terminal_outcome()
    {
        var directory = Directory.CreateTempSubdirectory("digitalbrain-scripting-");
        try
        {
            var key = new StartupExecutionKey("owner", "signal", "sha256");
            var completedAt = DateTimeOffset.UnixEpoch;
            var succeeded = StartupExecution.Succeeded(key, "started", completedAt);
            var failed = StartupExecution.Failed(key, "failed", ["boom"], completedAt.AddSeconds(1));
            var ledger = new FileStartupExecutionLedger(directory.FullName);

            await ledger.RecordAsync(succeeded, TestContext.Current.CancellationToken);
            await ledger.RecordAsync(failed, TestContext.Current.CancellationToken);

            var restored = await ledger.FindAsync(key, TestContext.Current.CancellationToken);

            Assert.Equal(succeeded, restored);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Failed_append_does_not_retain_an_unpersisted_execution()
    {
        var directory = Directory.CreateTempSubdirectory("digitalbrain-scripting-");
        try
        {
            var stateDirectory = Directory.CreateDirectory(Path.Combine(directory.FullName, "state"));
            Directory.CreateDirectory(Path.Combine(stateDirectory.FullName, "startup-executions.jsonl"));
            var key = new StartupExecutionKey("owner", "signal", "sha256");
            var execution = StartupExecution.Succeeded(key, "started", DateTimeOffset.UnixEpoch);
            var ledger = new FileStartupExecutionLedger(stateDirectory.FullName);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ledger.RecordAsync(
                execution,
                TestContext.Current.CancellationToken));

            var restored = await ledger.FindAsync(key, TestContext.Current.CancellationToken);

            Assert.Null(restored);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
