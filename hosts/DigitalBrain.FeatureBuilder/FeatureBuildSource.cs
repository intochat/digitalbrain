using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace DigitalBrain.FeatureBuilder;

internal static class FeatureBuildSource
{
    private const string TestingTargetsPath =
        "src/DigitalBrain.Features.Testing/buildTransitive/DigitalBrain.Features.Testing.targets";

    private static readonly HashSet<string> AllowedPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "DigitalBrain.Features.Sdk",
        "DigitalBrain.Features.Testing",
        "DigitalBrain.Integrations.Google.Contracts",
        "DigitalBrain.Integrations.Salesforce.Contracts",
        "Microsoft.NET.Test.Sdk",
        "Reqnroll",
        "Reqnroll.xUnit",
        "xunit",
        "xunit.runner.visualstudio"
    };

    private static readonly HashSet<string> TestOnlyPackages = new(StringComparer.OrdinalIgnoreCase)
    { "DigitalBrain.Features.Testing", "Microsoft.NET.Test.Sdk", "Reqnroll", "Reqnroll.xUnit", "xunit", "xunit.runner.visualstudio" };

    private static readonly HashSet<string> AllowedProperties = new(StringComparer.Ordinal)
    {
        "Description",
        "EnforceCodeStyleInBuild",
        "ImplicitUsings",
        "IsPackable",
        "IsTestProject",
        "ManagePackageVersionsCentrally",
        "Nullable",
        "PackageId",
        "PackageReadmeFile",
        "ReqnrollUseIntermediateOutputPathForCodeBehind",
        "SkipDeployBuild",
        "SkipFlutterBuild",
        "TargetFramework",
        "Version"
    };

    private static readonly HashSet<string> AllowedItems = new(StringComparer.Ordinal)
    {
        "AdditionalFiles",
        "Compile",
        "Content",
        "EmbeddedResource",
        "None",
        "PackageReference",
        "PackageVersion",
        "ProjectReference",
        "ReqnrollFeatureFile"
    };

    private static readonly HashSet<string> AllowedItemAttributes = new(StringComparer.Ordinal) { "CopyToOutputDirectory", "Include", "Link", "Pack", "PackagePath", "Remove", "Update", "Version" };

    internal static void Validate(FeatureSourceSnapshot snapshot)
    {
        var files = snapshot.Files.ToDictionary(static file => file.Path, StringComparer.OrdinalIgnoreCase);
        var executableTarget = files.Keys.FirstOrDefault(path =>
            path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase) &&
            !path.Equals(TestingTargetsPath, StringComparison.OrdinalIgnoreCase));
        if (executableTarget is not null)
        {
            throw Invalid($"Source build target '{executableTarget}' is forbidden.");
        }

        var centralVersions = files.Where(static pair => pair.Key.EndsWith(".props", StringComparison.OrdinalIgnoreCase))
            .SelectMany(pair => ParseXml(pair.Key, pair.Value.Content).Descendants("PackageVersion").Select(element => (Id: RequiredAttribute(element, "Include", pair.Key), Version: RequiredAttribute(element, "Version", pair.Key))))
            .GroupBy(static item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last().Version,
                StringComparer.OrdinalIgnoreCase);

        foreach (var file in snapshot.Files.Where(IsBuildFile))
        {
            var document = ParseXml(file.Path, file.Content);
            if (file.Path.Equals(TestingTargetsPath, StringComparison.OrdinalIgnoreCase))
            {
                ValidateTestingTargets(document);
                continue;
            }

            ValidateRoot(file.Path, document);
            ValidateBuildAuthority(file.Path, document);
            ValidateImports(file.Path, document, files);
            ValidateProjectReferences(file.Path, document, files, snapshot);
            ValidatePackages(file.Path, document, centralVersions, snapshot);
            ValidateItemPaths(file.Path, document, files);
        }

        ValidateScenarioProject(snapshot, files);
    }

    internal static async Task MaterializeAsync(string workspace, FeatureSourceSnapshot snapshot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(workspace);
        foreach (var file in snapshot.Files.OrderBy(static file => file.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = LocalPath(workspace, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllTextAsync(destination, file.Content, new UTF8Encoding(false), cancellationToken);
        }
    }

    internal static async Task<string> WriteNuGetConfigAsync(string workspace, string feed, CancellationToken cancellationToken)
    {
        var path = Path.Combine(workspace, "NuGet.Config");
        var document = new XDocument(
            new XElement("configuration",
                new XElement("packageSources", new XElement("clear"), new XElement("add", new XAttribute("key", "offline"), new XAttribute("value", feed))),
                new XElement("auditSources", new XElement("clear"))));
        await File.WriteAllTextAsync(path, document.ToString(SaveOptions.DisableFormatting), new UTF8Encoding(false), cancellationToken);
        return path;
    }

    internal static string LocalPath(string workspace, string relativePath) =>
        Path.Combine(workspace, relativePath.Replace('/', Path.DirectorySeparatorChar));

    internal static XDocument ParseXml(string path, string content)
    {
        try
        {
            using var stringReader = new StringReader(content);
            using var reader = XmlReader.Create(stringReader, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = true, XmlResolver = null });
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException exception)
        {
            throw new FeatureBuildException(FeatureBuildFailure.InvalidSource, $"'{path}' is not valid bounded XML.", exception);
        }
    }

    internal static int ExpectedScenarioCount(FeatureSourceSnapshot snapshot)
    {
        var scenarioDirectory = snapshot.ScenarioProjectPath[..snapshot.ScenarioProjectPath.LastIndexOf('/')];
        var scenarioProject = snapshot.Files.Single(file =>
            file.Path.Equals(snapshot.ScenarioProjectPath, StringComparison.OrdinalIgnoreCase));
        var project = ParseXml(scenarioProject.Path, scenarioProject.Content);
        var includesGeneratedDuplicate = project.Descendants("Import").Select(import => import.Attribute("Project")?.Value.Replace('\\', '/'))
            .Any(import =>
                import is not null && TryResolveVirtualPath(scenarioProject.Path, import, out var resolved) &&
                resolved.Equals(TestingTargetsPath, StringComparison.OrdinalIgnoreCase));
        var featureFiles = snapshot.Files.Where(file =>
            file.Path.EndsWith(".feature", StringComparison.OrdinalIgnoreCase) &&
            (file.Path.StartsWith(scenarioDirectory + "/", StringComparison.OrdinalIgnoreCase) ||
             includesGeneratedDuplicate && file.Path.Equals("src/DigitalBrain.Features.Testing/GeneratedDuplicateInput.feature", StringComparison.OrdinalIgnoreCase)));
        var count = 0;
        foreach (var file in featureFiles)
        {
            string? docStringDelimiter = null;
            foreach (var line in file.Content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var value = line.TrimStart();
                if (docStringDelimiter is not null)
                {
                    if (value.Equals(docStringDelimiter, StringComparison.Ordinal))
                    {
                        docStringDelimiter = null;
                    }

                    continue;
                }

                if (value.StartsWith("\"\"\"", StringComparison.Ordinal) || value.StartsWith("```", StringComparison.Ordinal))
                {
                    docStringDelimiter = value[..3];
                    continue;
                }

                if (value.StartsWith("# language:", StringComparison.OrdinalIgnoreCase))
                {
                    throw Invalid($"Only canonical English Gherkin is supported in '{file.Path}'.");
                }

                if (value.StartsWith('#'))
                {
                    continue;
                }

                if (value.StartsWith("Scenario Outline:", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("Scenario Template:", StringComparison.OrdinalIgnoreCase))
                {
                    throw Invalid($"Scenario outlines are not supported in bounded Feature proof '{file.Path}'.");
                }

                if (value.StartsWith("Scenario:", StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            if (docStringDelimiter is not null)
            {
                throw Invalid($"Gherkin doc string in '{file.Path}' is not closed.");
            }
        }

        if (count == 0)
        {
            throw Invalid("The scenario project must contain at least one bounded source scenario.");
        }

        return count;
    }

    private static bool IsBuildFile(FeatureSourceFile file) =>
        file.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
        file.Path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
        file.Path.Equals(TestingTargetsPath, StringComparison.OrdinalIgnoreCase);

    private static void ValidateTestingTargets(XDocument document)
    {
        var expected = XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <DigitalBrainGeneratedDuplicateFeature Condition="Exists('$(MSBuildThisFileDirectory)..\contentFiles\any\any\GeneratedDuplicateInput.feature')">$(MSBuildThisFileDirectory)..\contentFiles\any\any\GeneratedDuplicateInput.feature</DigitalBrainGeneratedDuplicateFeature>
                <DigitalBrainGeneratedDuplicateFeature Condition="'$(DigitalBrainGeneratedDuplicateFeature)' == '' And Exists('$(MSBuildThisFileDirectory)..\GeneratedDuplicateInput.feature')">$(MSBuildThisFileDirectory)..\GeneratedDuplicateInput.feature</DigitalBrainGeneratedDuplicateFeature>
                <DigitalBrainGeneratedDuplicateFeatureCopy>$(BaseIntermediateOutputPath)DigitalBrain.Features.Testing\GeneratedDuplicateInput.feature</DigitalBrainGeneratedDuplicateFeatureCopy>
              </PropertyGroup>
              <ItemGroup Condition="'$(IsTestProject)' == 'true'">
                <ReqnrollFeatureFile Remove="$(DigitalBrainGeneratedDuplicateFeatureCopy)" />
                <ReqnrollFeatureFile Include="$(DigitalBrainGeneratedDuplicateFeatureCopy)" Link="GeneratedDuplicateInput.feature" Condition="'$(DigitalBrainGeneratedDuplicateFeature)' != ''" />
              </ItemGroup>
              <Target Name="PrepareDigitalBrainGeneratedDuplicateFeature" BeforeTargets="CoreProcessReqnrollFeatureFilesInProject" Condition="'$(DigitalBrainGeneratedDuplicateFeature)' != ''">
                <MakeDir Directories="$(MSBuildProjectDirectory)\$(BaseIntermediateOutputPath)DigitalBrain.Features.Testing" />
                <Copy SourceFiles="$(DigitalBrainGeneratedDuplicateFeature)" DestinationFiles="$(MSBuildProjectDirectory)\$(DigitalBrainGeneratedDuplicateFeatureCopy)" SkipUnchangedFiles="true" />
              </Target>
            </Project>
            """);
        if (!XNode.DeepEquals(document, expected))
        {
            throw Invalid($"'{TestingTargetsPath}' must match the approved testing target.");
        }
    }

    private static void ValidateRoot(string path, XDocument document)
    {
        if (document.Root is null || document.Root.Name.NamespaceName.Length != 0)
        {
            throw Invalid($"'{path}' must use the canonical namespace-free MSBuild format.");
        }

        if (!path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(document.Root.Attribute("Sdk")?.Value, "Microsoft.NET.Sdk", StringComparison.Ordinal))
        {
            throw Invalid($"Project '{path}' must use Microsoft.NET.Sdk.");
        }

        var targetFramework = document.Descendants("TargetFramework").Select(static element => element.Value)
            .LastOrDefault();
        if (!string.Equals(targetFramework, "net11.0", StringComparison.Ordinal))
        {
            throw Invalid($"Project '{path}' must target net11.0 exactly.");
        }

        if (document.Descendants("AllowUnsafeBlocks").Any(static element => string.Equals(element.Value, "true", StringComparison.OrdinalIgnoreCase)))
        {
            throw Invalid($"Project '{path}' cannot enable unsafe code.");
        }
    }

    private static void ValidateBuildAuthority(string path, XDocument document)
    {
        var forbiddenProperty = document.Descendants("PropertyGroup").Elements().FirstOrDefault(element => !AllowedProperties.Contains(element.Name.LocalName));
        var forbiddenPropertyAttribute = document.Descendants("PropertyGroup").Elements().SelectMany(element => element.Attributes().Select(attribute => (element, attribute)))
            .FirstOrDefault(pair => !IsApprovedPropertyAttribute(path, pair.element, pair.attribute));
        var forbiddenGroupAttribute = document.Descendants().Where(element => element.Name.LocalName is "PropertyGroup" or "ItemGroup")
            .SelectMany(static element => element.Attributes())
            .FirstOrDefault();
        var forbiddenImportAttribute = document.Descendants("Import").SelectMany(static element => element.Attributes())
            .FirstOrDefault(attribute => !attribute.Name.LocalName.Equals("Project", StringComparison.Ordinal));
        var forbiddenItem = document.Descendants("ItemGroup").Elements().FirstOrDefault(element =>
                !AllowedItems.Contains(element.Name.LocalName) || element.Elements().Any() ||
                element.Attributes().Any(attribute =>
                    !AllowedItemAttributes.Contains(attribute.Name.LocalName)));
        var forbiddenRemoval = document.Descendants().FirstOrDefault(element =>
                element.Attribute("Remove") is not null && !IsApprovedTestingFeatureRemoval(path, element));
        if (document.Descendants("Exec").Any() || document.Descendants("UsingTask").Any() || document.Descendants("Analyzer").Any() ||
            document.Descendants("Reference").Any() ||
            document.Descendants("PackageDownload").Any() ||
            document.Descendants("DotNetCliToolReference").Any() ||
            document.ToString(SaveOptions.DisableFormatting).Contains("$([", StringComparison.Ordinal) ||
            document.Descendants().Any(element => element.Name.LocalName is
                "RestoreSources" or
                "RestoreAdditionalProjectSources" or
                "NuGetAuditSources" or
                "BaseIntermediateOutputPath" or
                "BaseOutputPath" or
                "IntermediateOutputPath" or
                "OutputPath" or
                "MSBuildProjectExtensionsPath" or
                "CustomBeforeMicrosoftCommonTargets" or
                "CustomAfterMicrosoftCommonTargets") ||
            forbiddenProperty is not null ||
            forbiddenPropertyAttribute.attribute is not null ||
            forbiddenGroupAttribute is not null ||
            forbiddenImportAttribute is not null ||
            forbiddenItem is not null)
        {
            throw Invalid($"Build-time execution or package-source overrides are forbidden in '{path}'.");
        }

        if (forbiddenRemoval is not null)
        {
            throw Invalid($"Source item '{forbiddenRemoval.Name.LocalName}' cannot remove compiled inputs in '{path}'.");
        }

        if (document.Descendants("Target").Any())
        {
            throw Invalid($"Custom build targets are forbidden in '{path}'.");
        }
    }

    private static bool IsApprovedPropertyAttribute(string path, XElement property, XAttribute attribute) =>
        path.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase) &&
        attribute.Name.LocalName.Equals("Condition", StringComparison.Ordinal) &&
        property.Name.LocalName is "SkipFlutterBuild" or "SkipDeployBuild" or "EnforceCodeStyleInBuild" &&
        attribute.Value.Equals($"'$({property.Name.LocalName})' == ''", StringComparison.Ordinal);

    private static bool IsApprovedTestingFeatureRemoval(string path, XElement element) =>
        path.Equals("src/DigitalBrain.Features.Testing/DigitalBrain.Features.Testing.csproj", StringComparison.OrdinalIgnoreCase) &&
        element.Name.LocalName.Equals("ReqnrollFeatureFile", StringComparison.Ordinal) &&
        element.Attributes().Count() == 1 &&
        element.Attribute("Remove")?.Value.Equals("GeneratedDuplicateInput.feature", StringComparison.Ordinal) == true;

    private static void ValidateImports(string sourcePath, XDocument document, IReadOnlyDictionary<string, FeatureSourceFile> files)
    {
        foreach (var import in document.Descendants("Import"))
        {
            var project = RequiredAttribute(import, "Project", sourcePath).Replace('\\', '/');
            var resolvedImport = TryResolveVirtualPath(sourcePath, project, out var resolved);
            var approvedTestingTarget = resolvedImport && resolved.Equals(TestingTargetsPath, StringComparison.OrdinalIgnoreCase);
            if (project.Contains("$(", StringComparison.Ordinal) || !resolvedImport || !files.ContainsKey(resolved) ||
                project.EndsWith(".targets", StringComparison.OrdinalIgnoreCase) && !approvedTestingTarget)
            {
                throw Invalid($"Import '{project}' in '{sourcePath}' is outside the source snapshot.");
            }
        }
    }

    private static void ValidateProjectReferences(string sourcePath, XDocument document, IReadOnlyDictionary<string, FeatureSourceFile> files, FeatureSourceSnapshot snapshot)
    {
        foreach (var reference in document.Descendants("ProjectReference"))
        {
            var include = RequiredAttribute(reference, "Include", sourcePath).Replace('\\', '/');
            if (include.Contains("$(", StringComparison.Ordinal) || !TryResolveVirtualPath(sourcePath, include, out var resolved) ||
                !files.ContainsKey(resolved))
            {
                throw Invalid($"Project reference '{include}' in '{sourcePath}' is outside the source snapshot.");
            }

            if (!IsApprovedProject(resolved, snapshot.ImplementationProjectPath))
            {
                throw Invalid($"Project reference '{include}' in '{sourcePath}' is not an approved Feature dependency.");
            }
        }
    }

    private static bool IsApprovedProject(string path, string implementationProjectPath)
    {
        if (string.Equals(path, implementationProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.Equals("src/DigitalBrain.Features.Sdk/DigitalBrain.Features.Sdk.csproj", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("src/DigitalBrain.Features.Testing/DigitalBrain.Features.Testing.csproj", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("integrations/DigitalBrain.Integrations.Google.Contracts/DigitalBrain.Integrations.Google.Contracts.csproj", StringComparison.OrdinalIgnoreCase) ||
            path.Equals(
                "integrations/DigitalBrain.Integrations.Salesforce.Contracts/DigitalBrain.Integrations.Salesforce.Contracts.csproj",
                StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidatePackages(string sourcePath, XDocument document, IReadOnlyDictionary<string, string> centralVersions, FeatureSourceSnapshot snapshot)
    {
        foreach (var package in document.Descendants("PackageReference"))
        {
            var id = RequiredAttribute(package, "Include", sourcePath);
            if (!AllowedPackages.Contains(id))
            {
                throw new FeatureBuildException(FeatureBuildFailure.ForbiddenPackage, $"Package '{id}' is not allowed in Feature builds.");
            }

            if (string.Equals(sourcePath, snapshot.ImplementationProjectPath, StringComparison.OrdinalIgnoreCase) && TestOnlyPackages.Contains(id))
            {
                throw new FeatureBuildException(FeatureBuildFailure.ForbiddenPackage, $"Test package '{id}' is not allowed in a Feature implementation.");
            }

            var version = package.Attribute("Version")?.Value;
            if (string.IsNullOrWhiteSpace(version) && !centralVersions.TryGetValue(id, out version))
            {
                throw Invalid($"Package '{id}' must have an exact offline version.");
            }

            if (!IsExactVersion(version))
            {
                throw Invalid($"Package '{id}' has non-deterministic version '{version}'.");
            }
        }
    }

    private static bool IsExactVersion(string version)
    {
        if (version.Length is 0 or > 64 || version.IndexOfAny(['*', '[', ']', '(', ')', ',']) >= 0)
        {
            return false;
        }

        var core = version.Split('-', 2)[0];
        var parts = core.Split('.');
        return parts.Length >= 3 && parts.All(static part =>
            part.Length > 0 && part.All(char.IsAsciiDigit));
    }

    private static void ValidateItemPaths(string sourcePath, XDocument document, IReadOnlyDictionary<string, FeatureSourceFile> files)
    {
        string[] itemNames =
        ["Compile", "EmbeddedResource", "Content", "None", "AdditionalFiles", "ReqnrollFeatureFile"];
        foreach (var item in itemNames.SelectMany(name => document.Descendants(name)))
        {
            var value = item.Attribute("Include")?.Value ?? item.Attribute("Update")?.Value;
            if (value is null)
            {
                continue;
            }

            var normalized = value.Replace('\\', '/');
            if (normalized.Contains("$(", StringComparison.Ordinal) || normalized.IndexOfAny(['*', '?', ';']) >= 0 ||
                !TryResolveVirtualPath(sourcePath, normalized, out var resolved) ||
                !files.ContainsKey(resolved))
            {
                throw Invalid($"Item path '{value}' in '{sourcePath}' is outside the source snapshot.");
            }

            var link = item.Attribute("Link")?.Value;
            if (link is not null &&
                (link.Contains("$(", StringComparison.Ordinal) || link.Contains("%(", StringComparison.Ordinal) || !IsCanonicalRelativePath(link)))
            {
                throw Invalid($"Item link path '{link}' in '{sourcePath}' is outside the build output.");
            }

            var copy = item.Attribute("CopyToOutputDirectory")?.Value;
            if (copy is not null && copy is not ("Never" or "PreserveNewest" or "Always"))
            {
                throw Invalid($"Item copy policy '{copy}' in '{sourcePath}' is not supported.");
            }
        }
    }

    private static bool IsCanonicalRelativePath(string path)
    {
        try
        {
            _ = FeatureSourceSnapshot.ValidatePath(path.Replace('\\', '/'), nameof(path));
            return !path.Contains('\\', StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void ValidateScenarioProject(FeatureSourceSnapshot snapshot, IReadOnlyDictionary<string, FeatureSourceFile> files)
    {
        var scenarioFile = files[snapshot.ScenarioProjectPath];
        var scenarioProject = ParseXml(scenarioFile.Path, scenarioFile.Content);
        var referencesImplementation = scenarioProject.Descendants("ProjectReference").Select(reference => RequiredAttribute(reference, "Include", scenarioFile.Path).Replace('\\', '/'))
            .Any(reference =>
                TryResolveVirtualPath(scenarioFile.Path, reference, out var resolved) &&
                resolved.Equals(snapshot.ImplementationProjectPath, StringComparison.OrdinalIgnoreCase));
        if (!referencesImplementation)
        {
            throw Invalid("The scenario project must reference the implementation project directly.");
        }

        var projectReferences = scenarioProject.Descendants("ProjectReference").Select(reference => RequiredAttribute(reference, "Include", scenarioFile.Path).Replace('\\', '/'))
            .Select(reference => TryResolveVirtualPath(scenarioFile.Path, reference, out var resolved) ? resolved : string.Empty);
        var packageReferences = scenarioProject.Descendants("PackageReference").Select(reference => RequiredAttribute(reference, "Include", scenarioFile.Path));
        if (!projectReferences.Any(path => path.Equals("src/DigitalBrain.Features.Testing/DigitalBrain.Features.Testing.csproj", StringComparison.OrdinalIgnoreCase)) &&
            !packageReferences.Any(id => id.Equals("DigitalBrain.Features.Testing", StringComparison.OrdinalIgnoreCase)))
        {
            throw Invalid("The scenario project must use DigitalBrain.Features.Testing.");
        }

        var scenarioDirectory = scenarioFile.Path[..scenarioFile.Path.LastIndexOf('/')];
        if (!files.Keys.Any(path =>
                path.StartsWith(scenarioDirectory + "/", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(".feature", StringComparison.OrdinalIgnoreCase)))
        {
            throw Invalid("The scenario project must contain at least one Gherkin feature file.");
        }

        _ = ExpectedScenarioCount(snapshot);

        var configPath = scenarioDirectory + "/reqnroll.json";
        if (!files.TryGetValue(configPath, out var config))
        {
            throw Invalid("The scenario project requires strict reqnroll.json configuration.");
        }

        try
        {
            using var json = JsonDocument.Parse(config.Content);
            var runtime = json.RootElement.GetProperty("runtime");
            if (!string.Equals(runtime.GetProperty("missingOrPendingStepsOutcome").GetString(), "Error", StringComparison.Ordinal) ||
                runtime.GetProperty("stopAtFirstError").GetBoolean())
            {
                throw Invalid("Reqnroll must fail missing or pending steps and report every step error.");
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FeatureBuildException(FeatureBuildFailure.InvalidSource, "The scenario project has invalid strict Reqnroll configuration.", exception);
        }
    }

    private static string RequiredAttribute(XElement element, string name, string path) =>
        element.Attribute(name)?.Value is { Length: > 0 } value
            ? value
            : throw Invalid($"Element '{element.Name.LocalName}' in '{path}' requires '{name}'.");

    private static bool TryResolveVirtualPath(string sourcePath, string reference, out string path)
    {
        var segments = new List<string>();
        foreach (var segment in sourcePath.Split('/').SkipLast(1).Concat(reference.Split('/')))
        {
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    path = string.Empty;
                    return false;
                }

                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(segment);
            }
        }

        path = string.Join('/', segments);
        return path.Length > 0;
    }

    private static FeatureBuildException Invalid(string message) =>
        new(FeatureBuildFailure.InvalidSource, message);
}
