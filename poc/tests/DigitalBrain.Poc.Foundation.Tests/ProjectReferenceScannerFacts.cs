using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace DigitalBrain.Poc.Foundation.Tests;

public sealed class ProjectReferenceScannerFacts
{
    [Fact]
    public void RejectsSiblingPrefixPathOutsidePoc()
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        var siblingProject = Path.Combine(fixture.SiblingPrefixRoot, "DigitalBrain.Scripting.csproj");
        File.WriteAllText(siblingProject, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        fixture.WriteProject("<Project><ItemGroup><ProjectReference Include=\"../poc-legacy/DigitalBrain.Scripting.csproj\" /></ItemGroup></Project>");

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    [Theory]
    [MemberData(nameof(InheritedLinkPathCases))]
    public void RejectsExternalItemsWithInheritedLinkMetadata(string itemName, string attributeName, string fileName)
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteProject($"""
            <Project>
              <ItemDefinitionGroup>
                <{itemName}>
                  <Link>{fileName}</Link>
                </{itemName}>
              </ItemDefinitionGroup>
              <ItemGroup>
                <{itemName} {attributeName}="../legacy/{fileName}" />
              </ItemGroup>
            </Project>
            """);

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    [Theory]
    [MemberData(nameof(CaseVariantPathBearingElementCases))]
    public void RejectsCaseVariantPathBearingElement(string element)
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteProject($"<Project>{element}</Project>");

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    [Fact]
    public void RejectsImportIntoIgnoredIntermediateDirectory()
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteIntermediateFile(
            "Injected.props",
            "<Project><ItemGroup><ProjectReference Include=\"../legacy/Legacy.csproj\" /></ItemGroup></Project>");
        fixture.WriteProject("<Project><Import Project=\"obj/Injected.props\" /></Project>");

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    [Fact]
    public void RejectsOutsideReferenceInTransitiveXmlImport()
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteAuthoredFile(
            "build/bridge.xml",
            "<Project><ItemGroup><ProjectReference Include=\"../../legacy/Legacy.csproj\" /></ItemGroup></Project>");
        fixture.WriteProject("<Project><Import Project=\"build/bridge.xml\" /></Project>");

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    [Fact]
    public void RejectsOutsideReferenceInImportedPropsResolvedFromImportingProjectDirectory()
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteAuthoredFile(
            "build/bridge.props",
            "<Project><ItemGroup><ProjectReference Include=\"../legacy/Legacy.csproj\" /></ItemGroup></Project>");
        var project = Path.Combine(fixture.PocRoot, "Boundary.csproj");
        fixture.WriteProject("<Project><Import Project=\"build/bridge.props\" /></Project>");

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));

        var output = GetProjectReferenceEvaluation(project);
        var expectedFullPath = Path.Combine(fixture.ParentRoot, "legacy", "Legacy.csproj").Replace("\\", "\\\\", StringComparison.Ordinal);
        Assert.Contains("\"FullPath\"", output, StringComparison.Ordinal);
        Assert.Contains(expectedFullPath, output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScansSharedImportForEachImportingProjectContext()
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteAuthoredFile(
            "build/shared.xml",
            "<Project><ItemGroup><ProjectReference Include=\"shared/Shared.csproj\" /></ItemGroup></Project>");
        fixture.WriteAuthoredFile("nested/Nested.csproj", "<Project><Import Project=\"../build/shared.xml\" /></Project>");
        fixture.WriteProject("<Project><Import Project=\"build/shared.xml\" /></Project>");

        var references = ProjectReferenceScanner.ReadAll(fixture.PocRoot);

        Assert.Contains(Path.Combine(fixture.PocRoot, "shared", "Shared.csproj"), references, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine(fixture.PocRoot, "nested", "shared", "Shared.csproj"), references, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    public void ScansImplicitDirectoryBuildFileForEachNestedProjectContext(string fileName)
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteAuthoredFile(
            $"nested/{fileName}",
            "<Project><ItemGroup><ProjectReference Include=\"../../legacy/Legacy.csproj\" /></ItemGroup></Project>");
        fixture.WriteAuthoredFile("nested/child/Child.csproj", "<Project />");

        var references = ProjectReferenceScanner.ReadAll(fixture.PocRoot);

        Assert.Contains(Path.Combine(fixture.PocRoot, "legacy", "Legacy.csproj"), references, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FollowsCyclicAuthoredImportsWithoutRecursion()
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteAuthoredFile("build/first.xml", "<Project><Import Project=\"second.xml\" /></Project>");
        fixture.WriteAuthoredFile("build/second.xml", "<Project><Import Project=\"first.xml\" /></Project>");
        fixture.WriteProject("<Project><Import Project=\"build/first.xml\" /></Project>");

        var references = ProjectReferenceScanner.ReadAll(fixture.PocRoot);

        Assert.Contains(Path.Combine(fixture.PocRoot, "build", "second.xml"), references, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsReferenceThroughSymlinkedDirectory()
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteOutsideFile("Legacy.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var linkedDirectory = Path.Combine(fixture.PocRoot, "linked");

        try
        {
            Directory.CreateSymbolicLink(linkedDirectory, fixture.OutsideRoot);
        }
        catch (Exception exception) when (exception is IOException || exception is PlatformNotSupportedException || exception is UnauthorizedAccessException)
        {
            throw new InvalidOperationException("The physical-containment regression requires a working directory symbolic link.", exception);
        }

        Assert.True(Directory.Exists(linkedDirectory), "The symlink regression fixture was not created.");
        fixture.WriteProject("<Project><ItemGroup><ProjectReference Include=\"linked/Legacy.csproj\" /></ItemGroup></Project>");

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    [Theory]
    [InlineData("./@(Outside)")]
    [InlineData("./%(Outside.Identity)")]
    [InlineData("./$(OutsideIdentity)")]
    public void RejectsEmbeddedMsBuildExpressionPath(string path)
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteProject($"""
            <Project>
              <ItemGroup>
                <Outside Include="../legacy/Legacy.csproj" />
              </ItemGroup>
              <Target Name="Probe">
                <ItemGroup>
                  <ProjectReference Include="{path}" />
                </ItemGroup>
              </Target>
            </Project>
            """);

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    [Theory]
    [MemberData(nameof(PercentEncodedTraversalPathCases))]
    public void RejectsPercentEncodedTraversalInEveryPathBearingConstruct(string projectContent)
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        var legacyDirectory = Path.Combine(fixture.ParentRoot, "legacy");
        Directory.CreateDirectory(legacyDirectory);
        File.WriteAllText(Path.Combine(legacyDirectory, "Legacy.targets"), "<Project />");
        fixture.WriteProject(projectContent);

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    [Theory]
    [InlineData("%24%28OutsideIdentity%29")]
    [InlineData("%40%28Outside%29")]
    [InlineData("%25%28Outside.Identity%29")]
    public void RejectsPercentEncodedMsBuildExpressionPath(string path)
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteProject($"<Project><ItemGroup><ProjectReference Include=\"{path}\" /></ItemGroup></Project>");

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    [Fact]
    public void PreservesPercentEncodedSemicolonAsSinglePathValue()
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteProject("<Project><ItemGroup><ProjectReference Include=\"safe%3Bname/Project.csproj\" /></ItemGroup></Project>");

        var references = ProjectReferenceScanner.ReadAll(fixture.PocRoot);

        Assert.Single(references);
        Assert.Contains(Path.Combine(fixture.PocRoot, "safe;name", "Project.csproj"), references, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsWildcardImportThatCanLoadAnExternalProjectReference()
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteAuthoredFile(
            "imports/bridge.props",
            "<Project><ItemGroup><ProjectReference Include=\"../legacy/Legacy.csproj\" /></ItemGroup></Project>");
        var project = Path.Combine(fixture.PocRoot, "Boundary.csproj");
        fixture.WriteProject("<Project><Import Project=\"imports/*.props\" /></Project>");

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));

        var output = GetProjectReferenceEvaluation(project);
        var expectedFullPath = Path.Combine(fixture.ParentRoot, "legacy", "Legacy.csproj").Replace("\\", "\\\\", StringComparison.Ordinal);
        Assert.Contains("\"FullPath\"", output, StringComparison.Ordinal);
        Assert.Contains(expectedFullPath, output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(WildcardProtectedItemPathCases))]
    public void RejectsWildcardPathInEveryProtectedItem(string projectContent)
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteProject(projectContent);

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    [Theory]
    [MemberData(nameof(HintPathMetadataCases))]
    public void RejectsExternalHintPathMetadata(string projectContent)
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteProject(projectContent);

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    [Fact]
    public void RejectsExternalPathInShadowedDirectoryBuildFile()
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteAuthoredFile(
            "Directory.Build.props",
            "<Project><ItemGroup><ProjectReference Include=\"../legacy/Legacy.csproj\" /></ItemGroup></Project>");
        fixture.WriteAuthoredFile("nested/Directory.Build.props", "<Project />");
        fixture.WriteAuthoredFile("nested/child/Child.csproj", "<Project />");

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    [Theory]
    [InlineData("orphan/Directory.Build.props")]
    [InlineData("orphan/Directory.Build.targets")]
    public void RejectsExternalPathInOrphanDirectoryBuildFile(string fileName)
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteAuthoredFile(
            fileName,
            "<Project><ItemGroup><ProjectReference Include=\"../../legacy/Legacy.csproj\" /></ItemGroup></Project>");
        fixture.WriteAuthoredFile("active/Active.csproj", "<Project />");

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    [Fact]
    public void RejectsExternalPathInSymlinkedOrphanDirectoryBuildFileFromPhysicalDirectory()
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteAuthoredFile(
            "physical/content.xml",
            "<Project><ItemGroup><ProjectReference Include=\"../../legacy/Legacy.csproj\" /></ItemGroup></Project>");
        var link = Path.Combine(fixture.PocRoot, "deep", "orphan", "Directory.Build.props");
        var linkDirectory = Path.GetDirectoryName(link) ?? throw new InvalidOperationException("Directory.Build link has no directory.");
        Directory.CreateDirectory(linkDirectory);

        try
        {
            File.CreateSymbolicLink(link, Path.Combine(fixture.PocRoot, "physical", "content.xml"));
        }
        catch (Exception exception) when (exception is IOException || exception is PlatformNotSupportedException || exception is UnauthorizedAccessException)
        {
            throw new InvalidOperationException("The physical-directory regression requires a working file symbolic link.", exception);
        }

        Assert.True(File.Exists(link), "The file symlink regression fixture was not created.");
        fixture.WriteAuthoredFile("active/Active.csproj", "<Project />");

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    [Fact]
    public void RejectsCreateItemOutputThatCreatesProjectReference()
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteProject("""
            <Project>
              <Target Name="InjectLegacy" BeforeTargets="ResolveProjectReferences">
                <CreateItem Include="../legacy/Legacy.csproj">
                  <Output TaskParameter="Include" ItemName="ProjectReference" />
                </CreateItem>
              </Target>
            </Project>
            """);

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    [Theory]
    [InlineData("<tArGeT Name=\"Custom\" />")]
    [InlineData("<uSiNgTaSk TaskName=\"Custom\" AssemblyFile=\"Custom.dll\" />")]
    public void RejectsAuthoredBuildExecutionElementsCaseInsensitively(string element)
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteProject($"<Project>{element}</Project>");

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    [Fact]
    public void IgnoresRestoreGeneratedIntermediateFiles()
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteIntermediateFile(
            "Boundary.csproj.nuget.g.props",
            "<Project><Target Name=\"Generated\"><CreateItem Include=\"../legacy/Legacy.csproj\"><Output TaskParameter=\"Include\" ItemName=\"ProjectReference\" /></CreateItem></Target></Project>");

        Assert.Empty(ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    [Theory]
    [MemberData(nameof(HandAuthoredResolverPropertyPathCases))]
    public void RejectsHandAuthoredResolverPropertyPathsInEveryPathBearingItem(string path, string item)
    {
        using var fixture = TemporaryMsBuildRoot.Create();
        fixture.WriteProject($"<Project>{string.Format(item, path)}</Project>");

        Assert.Throws<InvalidOperationException>(() => ProjectReferenceScanner.ReadAll(fixture.PocRoot));
    }

    public static IEnumerable<object?[]> HandAuthoredResolverPropertyPathCases()
    {
        string[] resolverProperties =
        [
            "$(NuGetPackageRoot)",
            "$(MSBuildSDKsPath)",
            "$(MSBuildExtensionsPath)",
        ];

        string[] pathBearingItems =
        [
            "<ItemGroup><ProjectReference Include=\"{0}..\\legacy\\Legacy.csproj\" /></ItemGroup>",
            "<ItemGroup><Compile Include=\"{0}..\\legacy\\Legacy.cs\" Link=\"Legacy.cs\" /></ItemGroup>",
            "<ItemGroup><Content Include=\"{0}..\\legacy\\Legacy.txt\" Link=\"Legacy.txt\" /></ItemGroup>",
            "<ItemGroup><None Include=\"{0}..\\legacy\\Legacy.json\" Link=\"Legacy.json\" /></ItemGroup>",
            "<Import Project=\"{0}..\\legacy\\Legacy.targets\" />",
            "<ItemGroup><Analyzer Include=\"{0}..\\legacy\\LegacyAnalyzer.dll\" /></ItemGroup>",
            "<ItemGroup><Reference Include=\"Legacy\"><HintPath>{0}..\\legacy\\Legacy.dll</HintPath></Reference></ItemGroup>",
        ];

        foreach (var resolverProperty in resolverProperties)
        {
            foreach (var pathBearingItem in pathBearingItems)
            {
                yield return [resolverProperty, pathBearingItem];
            }
        }
    }

    public static IEnumerable<object?[]> PercentEncodedTraversalPathCases()
    {
        yield return ["<Project><ItemGroup><ProjectReference Include=\"%2e%2e/legacy/Legacy.csproj\" /></ItemGroup></Project>"];
        yield return ["<Project><ItemGroup><Compile Include=\"%2e%2e/legacy/Legacy.cs\" /></ItemGroup></Project>"];
        yield return ["<Project><ItemGroup><Content Include=\"%2e%2e/legacy/Legacy.txt\" /></ItemGroup></Project>"];
        yield return ["<Project><ItemGroup><None Include=\"%2e%2e/legacy/Legacy.json\" /></ItemGroup></Project>"];
        yield return ["<Project><Import Project=\"%2e%2e/legacy/Legacy.targets\" /></Project>"];
        yield return ["<Project><ItemGroup><Analyzer Include=\"%2e%2e/legacy/LegacyAnalyzer.dll\" /></ItemGroup></Project>"];
        yield return ["<Project><ItemGroup><Reference Include=\"Legacy\"><HintPath>%2e%2e/legacy/Legacy.dll</HintPath></Reference></ItemGroup></Project>"];
    }

    public static IEnumerable<object?[]> WildcardProtectedItemPathCases()
    {
        yield return ["<Project><ItemGroup><ProjectReference Include=\"local/%2A.csproj\" /></ItemGroup></Project>"];
        yield return ["<Project><ItemGroup><Analyzer Include=\"local/*.dll\" /></ItemGroup></Project>"];
        yield return ["<Project><ItemGroup><Compile Include=\"local/*.cs\" /></ItemGroup></Project>"];
        yield return ["<Project><ItemGroup><Content Include=\"local/*.txt\" /></ItemGroup></Project>"];
        yield return ["<Project><ItemGroup><None Include=\"local/*.json\" /></ItemGroup></Project>"];
        yield return ["<Project><ItemGroup><Reference Include=\"Local\"><HintPath>local/%3F.dll</HintPath></Reference></ItemGroup></Project>"];
    }

    public static IEnumerable<object?[]> HintPathMetadataCases()
    {
        yield return ["<Project><ItemGroup><Reference Include=\"Legacy\"><HintPath>../legacy/Legacy.dll</HintPath></Reference></ItemGroup></Project>"];
        yield return ["<Project><ItemGroup><Reference Include=\"Legacy\" hInTpAtH=\"../legacy/Legacy.dll\" /></ItemGroup></Project>"];
        yield return ["<Project><ItemDefinitionGroup><Reference hInTpAtH=\"../legacy/Legacy.dll\" /></ItemDefinitionGroup><ItemGroup><Reference Include=\"Legacy\" /></ItemGroup></Project>"];
    }

    public static IEnumerable<object?[]> InheritedLinkPathCases()
    {
        string[] itemNames = ["Compile", "Content", "None"];
        string[] attributeNames = ["Include", "Update"];

        foreach (var itemName in itemNames)
        {
            foreach (var attributeName in attributeNames)
            {
                yield return [itemName, attributeName, $"Legacy.{itemName.ToLowerInvariant()}"];
            }
        }
    }

    public static IEnumerable<object?[]> CaseVariantPathBearingElementCases()
    {
        yield return ["<ItemGroup><projectreference Include=\"../legacy/Legacy.csproj\" /></ItemGroup>"];
        yield return ["<ItemGroup><aNaLyZeR Include=\"../legacy/LegacyAnalyzer.dll\" /></ItemGroup>"];
        yield return ["<ItemGroup><compile Include=\"../legacy/Legacy.cs\" /></ItemGroup>"];
        yield return ["<ItemGroup><cOnTeNt Include=\"../legacy/Legacy.txt\" /></ItemGroup>"];
        yield return ["<ItemGroup><none Include=\"../legacy/Legacy.json\" /></ItemGroup>"];
        yield return ["<iMpOrT Project=\"../legacy/Legacy.targets\" />"];
        yield return ["<ItemGroup><Reference Include=\"Legacy\"><hInTpAtH>../legacy/Legacy.dll</hInTpAtH></Reference></ItemGroup>"];
    }

    private static string GetProjectReferenceEvaluation(string project)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add("-getItem:ProjectReference");
        startInfo.ArgumentList.Add("-verbosity:quiet");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet msbuild.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"dotnet msbuild exited {process.ExitCode}.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        return standardOutput;
    }
}
