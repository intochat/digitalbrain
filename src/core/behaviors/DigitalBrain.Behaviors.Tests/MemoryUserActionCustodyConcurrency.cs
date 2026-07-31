using Xunit;

namespace DigitalBrain.Behaviors.Tests;

// Finding 7 adjudication: MemoryUserActionCustody is test/harness-only.
// Production registers GrainUserActionCustody in BehaviorsModule.ConfigureRuntime.
// Concurrent first-writer races are nonblocking for product and deferred.
public sealed class MemoryUserActionCustodyConcurrency
{
    [Fact(DisplayName =
        "MemoryUserActionCustody is test/harness-only — concurrent first-writer race deferred (nonblocking)")]
    public void MemoryUserActionCustodyIsTestOnlyConcurrentRaceDeferred()
    {
        var productionRegistration = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "core",
            "behaviors",
            "DigitalBrain.Behaviors.Runtime",
            "BehaviorsModule.Runtime.cs");
        Assert.True(File.Exists(productionRegistration));
        var runtime = File.ReadAllText(productionRegistration);
        Assert.Contains("GrainUserActionCustody", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "new MemoryUserActionCustody",
            runtime,
            StringComparison.Ordinal);

        var harness = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "modules",
            "tasks",
            "DigitalBrain.Modules.Tasks.Tests",
            "TasksHarnessModule.Runtime.cs");
        Assert.True(File.Exists(harness));
        var harnessSource = File.ReadAllText(harness);
        Assert.Contains("MemoryUserActionCustody", harnessSource, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find DigitalBrain.slnx above {AppContext.BaseDirectory}.");
    }
}
