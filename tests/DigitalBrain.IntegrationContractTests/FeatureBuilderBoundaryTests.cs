using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DigitalBrain.FeatureBuilder;
using Xunit;

namespace DigitalBrain.IntegrationContractTests;

public sealed class FeatureBuilderBoundaryTests
{
    [Theory]
    [InlineData("../escape.cs")]
    [InlineData("folder/../../escape.cs")]
    [InlineData("/rooted.cs")]
    [InlineData("C:/rooted.cs")]
    [InlineData("folder\\file.cs")]
    public void Source_paths_cannot_escape_the_snapshot(string path)
    {
        Assert.Throws<ArgumentException>(() => new FeatureSourceFile(path, "source"));
    }

    [Theory]
    [InlineData("src/COM¹/Feature.cs")]
    [InlineData("src/com¹.txt/Feature.cs")]
    [InlineData("src/COM²/Feature.cs")]
    [InlineData("src/cOm².json/Feature.cs")]
    [InlineData("src/COM³/Feature.cs")]
    [InlineData("src/Com³.cs/Feature.cs")]
    [InlineData("src/LPT¹/Feature.cs")]
    [InlineData("src/lpt¹.txt/Feature.cs")]
    [InlineData("src/LPT²/Feature.cs")]
    [InlineData("src/lPt².json/Feature.cs")]
    [InlineData("src/LPT³/Feature.cs")]
    [InlineData("src/Lpt³.cs/Feature.cs")]
    public void Windows_reserved_device_aliases_cannot_enter_the_source_snapshot(string path)
    {
        Assert.Throws<ArgumentException>(() => new FeatureSourceFile(path, "source"));
    }

    [Theory]
    [InlineData(" src/Feature.cs")]
    [InlineData("src /Feature.cs")]
    [InlineData("src/ Feature.cs")]
    [InlineData("src/Feature.cs ")]
    public void Source_path_segments_cannot_have_boundary_whitespace(string path)
    {
        Assert.Throws<ArgumentException>(() => new FeatureSourceFile(path, "source"));
    }

    [Theory]
    [InlineData("src./Feature.cs")]
    [InlineData("src/Feature.cs.")]
    public void Source_path_segments_cannot_end_with_a_dot(string path)
    {
        Assert.Throws<ArgumentException>(() => new FeatureSourceFile(path, "source"));
    }

    [Theory]
    [InlineData("src/Feature\u0001.cs")]
    [InlineData("src/Feature\u001F.cs")]
    [InlineData("src/Feature\u007F.cs")]
    [InlineData("src/Feature\u0085.cs")]
    [InlineData("src/Feature\u009F.cs")]
    public void Source_path_segments_cannot_contain_control_characters(string path)
    {
        Assert.Throws<ArgumentException>(() => new FeatureSourceFile(path, "source"));
    }

    [Theory]
    [InlineData("src/Feature<Name.cs")]
    [InlineData("src/Feature>Name.cs")]
    [InlineData("src/Feature:Name.cs")]
    [InlineData("src/Feature\"Name.cs")]
    [InlineData("src/Feature|Name.cs")]
    [InlineData("src/Feature?Name.cs")]
    [InlineData("src/Feature*Name.cs")]
    public void Source_path_segments_cannot_contain_windows_invalid_characters(string path)
    {
        Assert.Throws<ArgumentException>(() => new FeatureSourceFile(path, "source"));
    }

    [Theory]
    [InlineData("src/Feature.cs")]
    [InlineData("src/Δοκιμή/功能.cs")]
    public void Portable_source_paths_are_preserved(string path)
    {
        var file = new FeatureSourceFile(path, "source");

        Assert.Equal(path, file.Path);
    }

    [Fact]
    public void Source_snapshot_enforces_file_count_and_byte_limits()
    {
        var tooMany = Enumerable.Range(0, FeatureSourceSnapshot.MaximumFileCount + 1)
            .Select(index => new FeatureSourceFile($"file-{index}.cs", "source"))
            .ToArray();

        Assert.Throws<ArgumentException>(() => new FeatureSourceSnapshot(
            "feature/Feature.csproj",
            "feature.tests/Feature.Tests.csproj",
            tooMany));
        Assert.Throws<ArgumentException>(() => new FeatureSourceFile(
            "large.cs",
            new byte[FeatureSourceSnapshot.MaximumFileBytes + 1]));
        var largeFiles = Enumerable.Range(0, 5)
            .Select(index => new FeatureSourceFile(
                index switch
                {
                    0 => "feature/Feature.csproj",
                    1 => "feature.tests/Feature.Tests.csproj",
                    _ => $"file-{index}.cs"
                },
                new string('x', FeatureSourceSnapshot.MaximumFileBytes)))
            .ToArray();
        Assert.Throws<ArgumentException>(() => new FeatureSourceSnapshot(
            "feature/Feature.csproj",
            "feature.tests/Feature.Tests.csproj",
            largeFiles));
    }

    [Fact]
    public void FeatureBuilder_is_a_transient_dependency_free_executable()
    {
        var projectPath = Path.Combine(
            RepositoryRoot(),
            "hosts",
            "DigitalBrain.FeatureBuilder",
            "DigitalBrain.FeatureBuilder.csproj");
        var document = XDocument.Load(projectPath);

        Assert.Equal("Exe", Assert.Single(document.Descendants("OutputType")).Value);
        Assert.Empty(document.Descendants("ProjectReference"));
        Assert.Empty(document.Descendants("PackageReference"));
        var project = File.ReadAllText(projectPath);
        Assert.DoesNotContain("Orleans", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Aspire", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Azure", project, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Source_reference_is_canonical_order_independent_and_content_authoritative()
    {
        var first = new FeatureSourceSnapshot(
            "src/Feature.csproj",
            "tests/Feature.Tests.csproj",
            [
                new FeatureSourceFile("src/Feature.csproj", "implementation"),
                new FeatureSourceFile("tests/Feature.Tests.csproj", "scenarios")
            ]);
        var reversed = new FeatureSourceSnapshot(
            first.ImplementationProjectPath,
            first.ScenarioProjectPath,
            first.Files.Reverse().ToArray());
        var changed = new FeatureSourceSnapshot(
            first.ImplementationProjectPath,
            first.ScenarioProjectPath,
            [
                new FeatureSourceFile("src/Feature.csproj", "implementation changed"),
                new FeatureSourceFile("tests/Feature.Tests.csproj", "scenarios")
            ]);

        var reference = FeatureReleaseWriter.ComputeSourceReference(first);
        var kernelSource = new DigitalBrain.Kernel.Contracts.FeatureSourceSnapshot(
            first.ImplementationProjectPath,
            first.ScenarioProjectPath,
            first.Files.Select(file => new DigitalBrain.Kernel.Contracts.FeatureSourceFile(file.Path, file.Content)).ToArray());
        var kernelReference = DigitalBrain.Kernel.Features.FeatureDraftAuthoringTransitions.SourceReference(kernelSource);

        Assert.Matches("^sha256:[0-9a-f]{64}$", reference);
        Assert.Equal(kernelReference, reference);
        Assert.Equal(reference, FeatureReleaseWriter.ComputeSourceReference(reversed));
        Assert.NotEqual(reference, FeatureReleaseWriter.ComputeSourceReference(changed));
    }

    [Fact]
    public async Task Existing_content_address_rejects_extra_files()
    {
        using var directories = new TemporaryDirectories();
        File.WriteAllBytes(Path.Combine(directories.Build, "Feature.dll"), [1, 2, 3]);
        var writer = new FeatureReleaseWriter();
        var manifest = new FeatureManifest(
            "Feature.dll",
            "0.1.0.0",
            ["Example.Feature"],
            [],
            ["DigitalBrain.Features.Sdk"]);
        var scenarios = new FeatureScenarioResult(1, 1, 0, 0);
        var release = await writer.WriteAsync(
            directories.Output,
            "sha256:" + new string('0', 64),
            directories.Build,
            manifest,
            scenarios);
        File.WriteAllText(Path.Combine(release.ReleaseDirectory, "unexpected.txt"), "unexpected");

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() => writer.WriteAsync(
            directories.Output,
            release.SourceReference,
            directories.Build,
            manifest,
            scenarios));

        Assert.Equal(FeatureBuildFailure.ReleaseConflict, exception.Failure);
    }

    [Fact]
    public async Task Passing_verification_returns_ordered_scenario_evidence_artifacts_and_source_digest()
    {
        using var directories = new TemporaryDirectories();
        var pipeline = new FeatureBuildPipeline();

        var verification = await pipeline.VerifyAsync(VerificationRequest(VerificationSnapshot(), directories));

        Assert.NotNull(verification.Release);
        var release = verification.Release!;
        Assert.Equal(verification.SourceReference, release.SourceReference);
        Assert.Matches("^sha256:[0-9a-f]{64}$", verification.SourceReference);
        Assert.Equal(verification.Scenarios.Total, verification.Scenarios.Results.Count);
        Assert.Equal(verification.Scenarios.Total, verification.Scenarios.Passed);
        Assert.Equal(0, verification.Scenarios.Failed);
        Assert.Equal(0, verification.Scenarios.Skipped);
        Assert.All(verification.Scenarios.Results, result =>
        {
            Assert.Equal(FeatureScenarioOutcome.Passed, result.Outcome);
            Assert.Null(result.SafeFailure);
        });
        Assert.Equal(
            verification.Scenarios.Results.OrderBy(result => result.Name, StringComparer.Ordinal).ThenBy(result => result.ScenarioId, StringComparer.Ordinal),
            verification.Scenarios.Results);
        Assert.Equal(["scenarios.json", "source.json"], verification.Artifacts.Select(artifact => artifact.Name));
        Assert.Equal(verification.Artifacts, release.Artifacts);
        Assert.All(verification.Artifacts, AssertSafeArtifact);
    }

    [Fact]
    public async Task Failed_verification_preserves_safe_scenario_evidence_without_publishing_a_release()
    {
        using var directories = new TemporaryDirectories();
        var snapshot = AddFile(
            VerificationSnapshot(),
            "features/EmailSummarizer.Tests/EvidenceFailure.feature",
            "Feature: Evidence failure\n  Scenario: Bounded safe failure\n    Given a verification failure\n");
        snapshot = AddFile(
            snapshot,
            "features/EmailSummarizer.Tests/EvidenceFailureSteps.cs",
            "using Reqnroll; namespace DigitalBrain.Features.EmailSummarizer.Tests; [Binding] public sealed class EvidenceFailureSteps { [Given(\"a verification failure\")] public void Fail() => throw new InvalidOperationException(\"unsafe C:\\\\private\\\\secret.txt \" + new string('x', 4096)); }");

        var verification = await new FeatureBuildPipeline().VerifyAsync(VerificationRequest(snapshot, directories));

        Assert.Null(verification.Release);
        Assert.Matches("^sha256:[0-9a-f]{64}$", verification.SourceReference);
        Assert.Equal(verification.Scenarios.Total, verification.Scenarios.Results.Count);
        Assert.True(verification.Scenarios.Failed > 0);
        var failed = Assert.Single(
            verification.Scenarios.Results,
            result => result.Outcome == FeatureScenarioOutcome.Failed);
        Assert.NotNull(failed.SafeFailure);
        Assert.Contains("unsafe", failed.SafeFailure, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", failed.SafeFailure, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret.txt", failed.SafeFailure, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\n', failed.SafeFailure);
        Assert.InRange(failed.SafeFailure.Length, 1, 1024);
        Assert.Equal(["scenarios.json", "source.json"], verification.Artifacts.Select(artifact => artifact.Name));
        Assert.All(verification.Artifacts, AssertSafeArtifact);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directories.Output));
    }

    [Fact]
    public async Task FeatureBuilder_cli_returns_failed_verification_evidence_without_a_release()
    {
        using var directories = new TemporaryDirectories();
        var snapshot = AddFile(
            VerificationSnapshot(),
            "features/EmailSummarizer.Tests/CliFailure.feature",
            "Feature: CLI failure\n  Scenario: Returned failure\n    Given a CLI verification failure\n");
        var requestPath = Path.Combine(directories.Build, "request.json");
        var command = new
        {
            snapshot.ImplementationProjectPath,
            snapshot.ScenarioProjectPath,
            Files = snapshot.Files.Select(file => new
            {
                file.Path,
                ContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(file.Content))
            }).ToArray(),
            OfflineFeedDirectory = OfflineFeed(),
            OutputDirectory = directories.Output,
            Deadline = DateTimeOffset.UtcNow.Add(FeatureBuildPipeline.MaximumRequestDuration)
        };
        await File.WriteAllBytesAsync(requestPath, JsonSerializer.SerializeToUtf8Bytes(command));
        var start = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(FeatureBuildPipeline).Assembly.Location);
        start.ArgumentList.Add(requestPath);
        using var process = new Process { StartInfo = start };

        Assert.True(process.Start());
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;

        Assert.Equal(0, process.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        Assert.DoesNotContain(Path.GetTempPath(), output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(directories.Output, output, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Release").ValueKind);
        Assert.Matches("^sha256:[0-9a-f]{64}$", root.GetProperty("SourceReference").GetString());
        Assert.True(root.GetProperty("Scenarios").GetProperty("Failed").GetInt32() > 0);
        Assert.Equal(2, root.GetProperty("Artifacts").GetArrayLength());
        Assert.Empty(Directory.EnumerateFileSystemEntries(directories.Output));
    }

    [Fact]
    public async Task Forbidden_package_is_rejected_before_restore()
    {
        using var directories = new TemporaryDirectories();
        var project = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include="Newtonsoft.Json" Version="13.0.4" /></ItemGroup>
            </Project>
            """;
        var snapshot = new FeatureSourceSnapshot(
            "Feature.csproj",
            "Feature.csproj",
            [new FeatureSourceFile("Feature.csproj", project)]);
        var request = new FeatureBuildRequest(
            snapshot,
            directories.Feed,
            directories.Output,
            DateTimeOffset.UtcNow.AddSeconds(10));

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() =>
            new FeatureBuildPipeline().BuildAsync(request));

        Assert.Equal(FeatureBuildFailure.ForbiddenPackage, exception.Failure);
        Assert.Contains("Newtonsoft.Json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unrelated_test_project_cannot_replace_the_BDD_gate()
    {
        using var directories = new TemporaryDirectories();
        var project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>";
        var snapshot = new FeatureSourceSnapshot(
            "feature/Feature.csproj",
            "feature.tests/Feature.Tests.csproj",
            [
                new FeatureSourceFile("feature/Feature.csproj", project),
                new FeatureSourceFile("feature/Feature.cs", "namespace Example; public sealed class Feature {}"),
                new FeatureSourceFile("feature.tests/Feature.Tests.csproj", project),
                new FeatureSourceFile("feature.tests/Feature.feature", "Feature: Invalid\n  Scenario: Unrelated\n    Given a passing test\n"),
                new FeatureSourceFile("feature.tests/reqnroll.json", "{\"runtime\":{\"missingOrPendingStepsOutcome\":\"Error\",\"stopAtFirstError\":false}}")
            ]);

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() =>
            new FeatureBuildPipeline().BuildAsync(new FeatureBuildRequest(
                snapshot,
                directories.Feed,
                directories.Output,
                DateTimeOffset.UtcNow.AddSeconds(10))));

        Assert.Equal(FeatureBuildFailure.InvalidSource, exception.Failure);
        Assert.Contains("reference the implementation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_items_cannot_read_outside_the_source_snapshot()
    {
        using var directories = new TemporaryDirectories();
        var implementation = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
              <ItemGroup><Compile Include="../../outside.cs" /></ItemGroup>
            </Project>
            """;
        var scenario = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="../feature/Feature.csproj" /></ItemGroup>
            </Project>
            """;
        var snapshot = new FeatureSourceSnapshot(
            "feature/Feature.csproj",
            "feature.tests/Feature.Tests.csproj",
            [
                new FeatureSourceFile("feature/Feature.csproj", implementation),
                new FeatureSourceFile("feature/Feature.cs", "namespace Example; public sealed class Feature {}"),
                new FeatureSourceFile("feature.tests/Feature.Tests.csproj", scenario),
                new FeatureSourceFile("feature.tests/Feature.feature", "Feature: Invalid\n  Scenario: Invalid\n    Given invalid input\n"),
                new FeatureSourceFile("feature.tests/reqnroll.json", "{\"runtime\":{\"missingOrPendingStepsOutcome\":\"Error\",\"stopAtFirstError\":false}}")
            ]);

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() =>
            new FeatureBuildPipeline().BuildAsync(new FeatureBuildRequest(
                snapshot,
                directories.Feed,
                directories.Output,
                DateTimeOffset.UtcNow.AddSeconds(10))));

        Assert.Equal(FeatureBuildFailure.InvalidSource, exception.Failure);
        Assert.Contains("outside the source snapshot", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_build_item_types_cannot_add_compiler_inputs()
    {
        using var directories = new TemporaryDirectories();
        var project = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
              <ItemGroup><EditorConfigFiles Include="../../outside.editorconfig" /></ItemGroup>
            </Project>
            """;
        var snapshot = new FeatureSourceSnapshot(
            "Feature.csproj",
            "Feature.csproj",
            [new FeatureSourceFile("Feature.csproj", project)]);

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() =>
            new FeatureBuildPipeline().BuildAsync(new FeatureBuildRequest(
                snapshot,
                directories.Feed,
                directories.Output,
                DateTimeOffset.UtcNow.AddSeconds(10))));

        Assert.Equal(FeatureBuildFailure.InvalidSource, exception.Failure);
        Assert.Contains("Build-time execution", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copied_item_links_cannot_escape_the_build_output()
    {
        using var directories = new TemporaryDirectories();
        var project = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
              <ItemGroup><None Include="safe.txt" Link="../../outside.txt" CopyToOutputDirectory="Always" /></ItemGroup>
            </Project>
            """;
        var snapshot = new FeatureSourceSnapshot(
            "Feature.csproj",
            "Feature.csproj",
            [
                new FeatureSourceFile("Feature.csproj", project),
                new FeatureSourceFile("safe.txt", "safe")
            ]);

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() =>
            new FeatureBuildPipeline().BuildAsync(new FeatureBuildRequest(
                snapshot,
                directories.Feed,
                directories.Output,
                DateTimeOffset.UtcNow.AddSeconds(10))));

        Assert.Equal(FeatureBuildFailure.InvalidSource, exception.Failure);
        Assert.Contains("link path", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BaseIntermediateOutputPath")]
    [InlineData("DocumentationFile")]
    [InlineData("ErrorLog")]
    [InlineData("ArtifactsPath")]
    [InlineData("ApplicationIcon")]
    [InlineData("ApplicationManifest")]
    [InlineData("Win32Resource")]
    [InlineData("AssemblyOriginatorKeyFile")]
    [InlineData("DigitalBrainGeneratedDuplicateFeature")]
    [InlineData("DigitalBrainGeneratedDuplicateFeatureCopy")]
    public async Task Source_cannot_control_build_authority_properties(string propertyName)
    {
        using var directories = new TemporaryDirectories();
        var project = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <PROPERTY_NAME>../../outside</PROPERTY_NAME>
              </PropertyGroup>
            </Project>
            """.Replace("PROPERTY_NAME", propertyName, StringComparison.Ordinal);
        var snapshot = new FeatureSourceSnapshot(
            "Feature.csproj",
            "Feature.csproj",
            [new FeatureSourceFile("Feature.csproj", project)]);

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() =>
            new FeatureBuildPipeline().BuildAsync(new FeatureBuildRequest(
                snapshot,
                directories.Feed,
                directories.Output,
                DateTimeOffset.UtcNow.AddSeconds(10))));

        Assert.Equal(FeatureBuildFailure.InvalidSource, exception.Failure);
        Assert.Contains("Build-time execution", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allowed_properties_cannot_probe_host_state_with_conditions()
    {
        using var directories = new TemporaryDirectories();
        var project = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <Version Condition="Exists('C:/host-marker')">1.2.3</Version>
              </PropertyGroup>
            </Project>
            """;
        var snapshot = new FeatureSourceSnapshot(
            "Feature.csproj",
            "Feature.csproj",
            [new FeatureSourceFile("Feature.csproj", project)]);

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() =>
            new FeatureBuildPipeline().BuildAsync(new FeatureBuildRequest(
                snapshot,
                directories.Feed,
                directories.Output,
                DateTimeOffset.UtcNow.AddSeconds(10))));

        Assert.Equal(FeatureBuildFailure.InvalidSource, exception.Failure);
        Assert.Contains("Build-time execution", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scenario_project_cannot_remove_compiled_inputs()
    {
        using var directories = new TemporaryDirectories();
        var project = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
              <ItemGroup><ReqnrollFeatureFile Remove="Feature.feature" /></ItemGroup>
            </Project>
            """;
        var snapshot = new FeatureSourceSnapshot(
            "Feature.csproj",
            "Feature.csproj",
            [
                new FeatureSourceFile("Feature.csproj", project),
                new FeatureSourceFile("Feature.feature", "Feature: Proof\n  Scenario: Required\n    Given required input\n")
            ]);

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() =>
            new FeatureBuildPipeline().BuildAsync(new FeatureBuildRequest(
                snapshot,
                directories.Feed,
                directories.Output,
                DateTimeOffset.UtcNow.AddSeconds(10))));

        Assert.Equal(FeatureBuildFailure.InvalidSource, exception.Failure);
        Assert.Contains("cannot remove", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approved_testing_target_cannot_be_modified_by_feature_source()
    {
        using var directories = new TemporaryDirectories();
        var project = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup>
            </Project>
            """;
        var snapshot = new FeatureSourceSnapshot(
            "Feature.csproj",
            "Feature.csproj",
            [
                new FeatureSourceFile("Feature.csproj", project),
                new FeatureSourceFile(
                    "src/DigitalBrain.Features.Testing/buildTransitive/DigitalBrain.Features.Testing.targets",
                    "<Project><Target Name=\"Injected\"><Exec Command=\"echo unsafe\" /></Target></Project>")
            ]);

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() =>
            new FeatureBuildPipeline().BuildAsync(new FeatureBuildRequest(
                snapshot,
                directories.Feed,
                directories.Output,
                DateTimeOffset.UtcNow.AddSeconds(10))));

        Assert.Equal(FeatureBuildFailure.InvalidSource, exception.Failure);
        Assert.Contains("approved testing target", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expired_deadline_is_rejected_before_materialization()
    {
        using var directories = new TemporaryDirectories();
        var snapshot = new FeatureSourceSnapshot(
            "Feature.csproj",
            "Feature.Tests.csproj",
            [
                new FeatureSourceFile("Feature.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />"),
                new FeatureSourceFile("Feature.Tests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />")
            ]);
        var request = new FeatureBuildRequest(
            snapshot,
            directories.Feed,
            directories.Output,
            DateTimeOffset.UtcNow.AddSeconds(-1));

        var exception = await Assert.ThrowsAsync<FeatureBuildException>(() =>
            new FeatureBuildPipeline().BuildAsync(request));

        Assert.Equal(FeatureBuildFailure.DeadlineExceeded, exception.Failure);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directories.Output));
    }

    private static void AssertSafeArtifact(FeatureVerificationArtifact artifact)
    {
        Assert.False(Path.IsPathRooted(artifact.Name));
        Assert.DoesNotContain('/', artifact.Name);
        Assert.DoesNotContain('\\', artifact.Name);
        Assert.InRange(artifact.Name.Length, 1, 64);
        Assert.InRange(artifact.MediaType.Length, 1, 128);
        Assert.InRange(artifact.SizeBytes, 1, 1_048_576);
        Assert.Matches("^sha256:[0-9a-f]{64}$", artifact.Digest);
    }

    private static FeatureSourceSnapshot VerificationSnapshot()
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
        return new FeatureSourceSnapshot(
            "features/EmailSummarizer/DigitalBrain.Features.EmailSummarizer.csproj",
            "features/EmailSummarizer.Tests/DigitalBrain.Features.EmailSummarizer.Tests.csproj",
            paths.Select(path => new FeatureSourceFile(
                path,
                File.ReadAllBytes(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)))))
                .ToArray());
    }

    private static FeatureSourceSnapshot AddFile(FeatureSourceSnapshot snapshot, string path, string content) =>
        new(
            snapshot.ImplementationProjectPath,
            snapshot.ScenarioProjectPath,
            [.. snapshot.Files, new FeatureSourceFile(path, content)]);

    private static FeatureBuildRequest VerificationRequest(FeatureSourceSnapshot snapshot, TemporaryDirectories directories) =>
        new(snapshot, OfflineFeed(), directories.Output, DateTimeOffset.UtcNow.Add(FeatureBuildPipeline.MaximumRequestDuration));

    private static string OfflineFeed()
    {
        var configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
            : configured;
    }

    private sealed class TemporaryDirectories : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "digitalbrain-feature-builder-boundary",
            Guid.NewGuid().ToString("N"));

        public TemporaryDirectories()
        {
            Feed = Directory.CreateDirectory(Path.Combine(_root, "feed")).FullName;
            Output = Directory.CreateDirectory(Path.Combine(_root, "output")).FullName;
            Build = Directory.CreateDirectory(Path.Combine(_root, "build")).FullName;
        }

        public string Feed { get; }
        public string Output { get; }
        public string Build { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Brain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
