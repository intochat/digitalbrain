using DigitalBrain.FeatureBuilder;
using System.Xml.Linq;
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
