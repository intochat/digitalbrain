using System.Diagnostics;
using DigitalBrain.FeatureBuilder;
using DigitalBrain.FeatureHost;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Kernel.Contracts;
using Xunit;

namespace DigitalBrain.E2ETests;

[CollectionDefinition("FeatureBuilder process tests", DisableParallelization = true)]
public sealed class FeatureBuilderProcessCollection
{
}

[Collection("FeatureBuilder process tests")]
public sealed class FeatureBuilderReleaseE2ETests : FeatureBuilderE2ETestBase
{
    [Fact]
    public async Task Email_summarizer_build_produces_a_deterministic_immutable_release()
    {
        using var firstOutput = new TemporaryDirectory();
        using var secondOutput = new TemporaryDirectory();
        var snapshot = EmailSummarizerSnapshot();
        var reversed = new FeatureSourceSnapshot(
            snapshot.ImplementationProjectPath,
            snapshot.ScenarioProjectPath,
            snapshot.Files.Reverse().ToArray());
        var pipeline = new FeatureBuildPipeline();

        var first = await pipeline.BuildAsync(Request(snapshot, firstOutput.Path));
        var second = await pipeline.BuildAsync(Request(reversed, secondOutput.Path));

        Assert.Equal(first.Digest, second.Digest);
        Assert.Equal(first.SourceReference, second.SourceReference);
        Assert.Equal("DigitalBrain.Features.EmailSummarizer.dll", first.Manifest.ImplementationAssembly);
        Assert.Equal(
            ["DigitalBrain.Features.EmailSummarizer.EmailSummarizerFeature"],
            first.Manifest.FeatureTypes);
        Assert.Equal(["google.gmail.message.read.v1"], first.Manifest.RequestedCapabilities);
        Assert.Equal(4, first.Scenarios.Passed);
        Assert.Equal(0, first.Scenarios.Failed);
        Assert.Equal(0, first.Scenarios.Skipped);
        Assert.True(first.ReleaseWriteDuration < TimeSpan.FromSeconds(5));
        Assert.True(File.Exists(Path.Combine(first.ReleaseDirectory, "implementation", first.Manifest.ImplementationAssembly)));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(first.ReleaseDirectory, "*", SearchOption.AllDirectories),
            path => Path.GetExtension(path) is ".zip" or ".nupkg" or ".dbpkg");
        AssertReleaseFilesEqual(first.ReleaseDirectory, second.ReleaseDirectory);
        await VerifyRestartReloadAsync(first.ReleaseDirectory, first.Digest);
    }
}

[Collection("FeatureBuilder process tests")]
public sealed class FeatureBuilderBddE2ETests : FeatureBuilderE2ETestBase
{
    [Theory]
    [InlineData(ScenarioFailure.Undefined, FeatureBuildFailure.ScenarioFailed)]
    [InlineData(ScenarioFailure.Pending, FeatureBuildFailure.ScenarioFailed)]
    [InlineData(ScenarioFailure.Ambiguous, FeatureBuildFailure.ScenarioFailed)]
    public async Task Strict_BDD_failures_reject_the_release(
        ScenarioFailure failure,
        FeatureBuildFailure expected)
    {
        using var output = new TemporaryDirectory();
        var snapshot = WithScenarioFailure(EmailSummarizerSnapshot(), failure);

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() =>
            new FeatureBuildPipeline().BuildAsync(Request(snapshot, output.Path)));

        Assert.Equal(expected, exception.Failure);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output.Path));
    }
}

[Collection("FeatureBuilder process tests")]
public sealed class FeatureBuilderSecurityE2ETests : FeatureBuilderE2ETestBase
{
    [Fact]
    public async Task Nondeterministic_framework_input_rejects_the_release()
    {
        using var output = new TemporaryDirectory();
        var snapshot = AddFile(
            EmailSummarizerSnapshot(),
            "features/EmailSummarizer/Nondeterministic.cs",
            "namespace DigitalBrain.Features.EmailSummarizer; public static class Nondeterministic { public static System.DateTime Value => System.DateTime.UtcNow; }");

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() =>
            new FeatureBuildPipeline().BuildAsync(Request(snapshot, output.Path)));

        Assert.Equal(FeatureBuildFailure.NondeterministicInput, exception.Failure);
        Assert.Contains("System.DateTime.UtcNow", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scenario_network_code_is_rejected_before_execution()
    {
        using var output = new TemporaryDirectory();
        var snapshot = WithScenarioFailure(EmailSummarizerSnapshot(), ScenarioFailure.Network);

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() =>
            new FeatureBuildPipeline().BuildAsync(Request(snapshot, output.Path)));

        Assert.Equal(FeatureBuildFailure.NondeterministicInput, exception.Failure);
        Assert.Contains("System.Net", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output.Path));
    }

}

[Collection("FeatureBuilder process tests")]
public sealed class FeatureBuilderDeadlineE2ETests : FeatureBuilderE2ETestBase
{
    [Fact]
    public async Task Forged_reqnroll_metadata_cannot_inflate_the_compiled_scenario_count()
    {
        using var output = new TemporaryDirectory();
        var snapshot = AddFile(
            EmailSummarizerSnapshot(),
            "features/EmailSummarizer.Tests/ForgedScenario.cs",
            "namespace DigitalBrain.Features.EmailSummarizer.Tests; [System.CodeDom.Compiler.GeneratedCode(\"Reqnroll\", \"3.3.4\")] public sealed class ForgedScenario { [Xunit.Fact, Xunit.Trait(\"FeatureTitle\", \"Email Summarizer\")] public void Pass() {} }");

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() =>
            new FeatureBuildPipeline().BuildAsync(Request(snapshot, output.Path)));

        Assert.Equal(FeatureBuildFailure.InvalidSource, exception.Failure);
        Assert.Contains("source scenarios", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output.Path));
    }

    [Fact]
    public async Task Gherkin_doc_strings_cannot_create_phantom_source_scenarios()
    {
        using var output = new TemporaryDirectory();
        var snapshot = AddFile(
            EmailSummarizerSnapshot(),
            "features/EmailSummarizer.Tests/DocString.feature",
            "Feature: Doc string\n  Scenario: Doc string content is data\n    Given a doc string value\n      \"\"\"\n      Scenario: Phantom\n      \"\"\"\n");
        snapshot = AddFile(
            snapshot,
            "features/EmailSummarizer.Tests/DocStringSteps.cs",
            "using Reqnroll; namespace DigitalBrain.Features.EmailSummarizer.Tests; [Binding] public sealed class DocStringSteps { [Given(\"a doc string value\")] public void Read(string value) {} }");
        snapshot = AddFile(
            snapshot,
            "features/EmailSummarizer.Tests/DocStringForgedSteps.cs",
            "namespace DigitalBrain.Features.EmailSummarizer.Tests; [System.CodeDom.Compiler.GeneratedCode(\"Reqnroll\", \"3.3.4\")] public sealed class ForgedDocStringScenario { [Xunit.Fact, Xunit.Trait(\"FeatureTitle\", \"Doc string\")] public void Pass() {} }");

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() =>
            new FeatureBuildPipeline().BuildAsync(Request(snapshot, output.Path)));

        Assert.Equal(FeatureBuildFailure.InvalidSource, exception.Failure);
        Assert.Contains("source scenarios", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output.Path));
    }

    [Fact]
    public async Task Scenario_dependency_network_code_is_rejected_before_execution()
    {
        using var output = new TemporaryDirectory();
        var snapshot = AddFile(
            EmailSummarizerSnapshot(),
            "src/DigitalBrain.Features.Testing/UnsafeScenarioDependency.cs",
            "namespace DigitalBrain.Features.Testing; public static class UnsafeScenarioDependency { public static async Task ReadAsync() => await new System.Net.Http.HttpClient().GetStringAsync(\"https://example.com\"); }");

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() =>
            new FeatureBuildPipeline().BuildAsync(Request(snapshot, output.Path)));

        Assert.Equal(FeatureBuildFailure.NondeterministicInput, exception.Failure);
        Assert.Contains("System.Net", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output.Path));
    }

    [Fact]
    public async Task Deadline_kills_a_blocking_scenario_process()
    {
        using var output = new TemporaryDirectory();
        var snapshot = WithScenarioFailure(EmailSummarizerSnapshot(), ScenarioFailure.Blocking);
        var request = new FeatureBuildRequest(
            snapshot,
            OfflineFeed(),
            output.Path,
            DateTimeOffset.UtcNow.AddSeconds(5));
        var stopwatch = Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() =>
            new FeatureBuildPipeline().BuildAsync(request));

        Assert.Equal(FeatureBuildFailure.DeadlineExceeded, exception.Failure);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15));
        Assert.Empty(Directory.EnumerateFileSystemEntries(output.Path));
    }
}

public abstract class FeatureBuilderE2ETestBase
{
    protected static FeatureBuildRequest Request(FeatureSourceSnapshot snapshot, string output) =>
        new(snapshot, OfflineFeed(), output, DateTimeOffset.UtcNow.Add(FeatureBuildPipeline.MaximumRequestDuration));

    protected static string OfflineFeed()
    {
        var configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
            : configured;
    }

    protected static FeatureSourceSnapshot EmailSummarizerSnapshot()
    {
        var root = RepositoryRoot();
        string[] paths =
        [
            "Directory.Build.props",
            "Directory.Packages.props",
            "README.md",
            "src/DigitalBrain.Features.Sdk/DigitalBrain.Features.Sdk.csproj",
            "src/DigitalBrain.Features.Sdk/FeatureContracts.cs",
            "src/DigitalBrain.Features.Sdk/FeatureContext.cs",
            "src/DigitalBrain.Features.Sdk/MemoryContracts.cs",
            "integrations/DigitalBrain.Integrations.Google.Contracts/DigitalBrain.Integrations.Google.Contracts.csproj",
            "integrations/DigitalBrain.Integrations.Google.Contracts/GoogleCapabilities.cs",
            "integrations/DigitalBrain.Integrations.Google.Contracts/GmailContracts.cs",
            "src/DigitalBrain.Features.Testing/DigitalBrain.Features.Testing.csproj",
            "src/DigitalBrain.Features.Testing/FeatureDuplicateScenario.cs",
            "src/DigitalBrain.Features.Testing/FeatureScenarioContext.cs",
            "src/DigitalBrain.Features.Testing/FeatureScenarioSteps.cs",
            "src/DigitalBrain.Features.Testing/GeneratedDuplicateInput.feature",
            "src/DigitalBrain.Features.Testing/buildTransitive/DigitalBrain.Features.Testing.targets",
            "features/EmailSummarizer/DigitalBrain.Features.EmailSummarizer.csproj",
            "features/EmailSummarizer/EmailSummarizer.cs",
            "features/EmailSummarizer.Tests/DigitalBrain.Features.EmailSummarizer.Tests.csproj",
            "features/EmailSummarizer.Tests/EmailSummarizer.feature",
            "features/EmailSummarizer.Tests/EmailSummarizerSteps.cs",
            "features/EmailSummarizer.Tests/reqnroll.json"
        ];
        var files = paths
            .Select(path => new FeatureSourceFile(
                path,
                File.ReadAllBytes(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)))))
            .ToArray();
        return new FeatureSourceSnapshot(
            "features/EmailSummarizer/DigitalBrain.Features.EmailSummarizer.csproj",
            "features/EmailSummarizer.Tests/DigitalBrain.Features.EmailSummarizer.Tests.csproj",
            files);
    }

    protected static FeatureSourceSnapshot WithScenarioFailure(
        FeatureSourceSnapshot snapshot,
        ScenarioFailure failure)
    {
        var feature = failure switch
        {
            ScenarioFailure.Undefined => "Feature: Invalid\n  Scenario: Undefined\n    Given an undefined builder step\n",
            ScenarioFailure.Pending => "Feature: Invalid\n  Scenario: Pending\n    Given a pending builder step\n",
            ScenarioFailure.Ambiguous => "Feature: Invalid\n  Scenario: Ambiguous\n    Given an ambiguous builder step\n",
            ScenarioFailure.Blocking => "Feature: Invalid\n  Scenario: Blocking\n    Given a blocking builder step\n",
            ScenarioFailure.Network => "Feature: Invalid\n  Scenario: Network\n    Given a network builder step\n",
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
        var bindings = failure switch
        {
            ScenarioFailure.Pending => "using Reqnroll; namespace DigitalBrain.Features.EmailSummarizer.Tests; [Binding] public sealed class BuilderFailureSteps { [Given(\"a pending builder step\")] public void Pending() => throw new PendingStepException(); }",
            ScenarioFailure.Ambiguous => "using Reqnroll; namespace DigitalBrain.Features.EmailSummarizer.Tests; [Binding] public sealed class BuilderFailureSteps { [Given(\"a ambiguous builder step\")] public void First() {} [Given(\"a ambiguous builder step\")] public void Second() {} }",
            ScenarioFailure.Blocking => "using Reqnroll; namespace DigitalBrain.Features.EmailSummarizer.Tests; [Binding] public sealed class BuilderFailureSteps { [Given(\"a blocking builder step\")] public void Block() => Thread.Sleep(Timeout.Infinite); }",
            ScenarioFailure.Network => "using Reqnroll; namespace DigitalBrain.Features.EmailSummarizer.Tests; [Binding] public sealed class BuilderFailureSteps { [Given(\"a network builder step\")] public async Task Network() => await new System.Net.Http.HttpClient().GetStringAsync(\"https://example.com\"); }",
            _ => string.Empty
        };
        var updated = AddFile(
            snapshot,
            "features/EmailSummarizer.Tests/BuilderFailure.feature",
            feature);
        return string.IsNullOrEmpty(bindings)
            ? updated
            : AddFile(updated, "features/EmailSummarizer.Tests/BuilderFailureSteps.cs", bindings);
    }

    protected static FeatureSourceSnapshot AddFile(
        FeatureSourceSnapshot snapshot,
        string path,
        string content) =>
        new(
            snapshot.ImplementationProjectPath,
            snapshot.ScenarioProjectPath,
            [.. snapshot.Files, new FeatureSourceFile(path, content)]);

    protected static void AssertReleaseFilesEqual(string first, string second)
    {
        var firstFiles = Directory.EnumerateFiles(first, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(first, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var secondFiles = Directory.EnumerateFiles(second, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(second, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(firstFiles, secondFiles);
        foreach (var path in firstFiles)
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(first, path.Replace('/', Path.DirectorySeparatorChar))),
                File.ReadAllBytes(Path.Combine(second, path.Replace('/', Path.DirectorySeparatorChar))));
        }
    }

    protected static async Task VerifyRestartReloadAsync(string releaseDirectory, string digest)
    {
        var installation = new FeatureInstallationId("email-summarizer-e2e");
        var descriptor = new FeatureReleaseDescriptor(new ReleaseDigest(digest), releaseDirectory);
        var owner = new BrainOwnerId("e2e-owner");
        await using (var manager = Manager())
        {
            await manager.ActivateAsync(owner, installation, descriptor);
            using var lease = manager.Acquire(owner, installation, descriptor.Digest);
            Assert.Equal(descriptor.Digest, lease.Digest);
        }

        await using (var restarted = Manager())
        {
            await restarted.LoadActiveAsync([new FeatureActiveInstallation(owner, installation, descriptor)]);
            using var lease = restarted.Acquire(owner, installation, descriptor.Digest);
            Assert.Equal(descriptor.Digest, lease.Digest);
        }
    }

    private static FeatureReleaseManager Manager() => new(
        new SingleServiceProvider(new GmailReader()),
        new NoopRecycle());

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Brain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    public enum ScenarioFailure
    {
        Undefined,
        Pending,
        Ambiguous,
        Blocking,
        Network
    }

    private sealed class SingleServiceProvider(object service) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType.IsInstanceOfType(service) ? service : null;
    }

    private sealed class GmailReader : IGmailMessageReader
    {
        public Task<GmailMessage> ReadAsync(
            GmailMessageReadRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoopRecycle : IFeatureHostRecycle
    {
        public void RequestRecycle()
        {
        }
    }

    protected sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateDirectory(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "digitalbrain-feature-builder-e2e",
                Guid.NewGuid().ToString("N"))).FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
