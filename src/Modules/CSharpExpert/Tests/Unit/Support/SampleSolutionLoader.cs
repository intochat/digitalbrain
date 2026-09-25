using DigitalBrain.Microsoft.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DigitalBrain.Tests;

// The Understand test proves the behavior maps a real solution; this loader keeps that deterministic
// and offline by building an AdhocWorkspace from the sample files instead of running an MSBuild load.
internal sealed class SampleSolutionLoader : ISolutionLoader
{
    public Task<LoadedSolution> OpenAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(solutionPath)
            ?? throw new InvalidOperationException($"'{solutionPath}' has no directory.");
        var projectFiles = Directory.GetFiles(directory, "*.csproj", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var byPath = new Dictionary<string, ProjectId>(StringComparer.OrdinalIgnoreCase);
        foreach (var projectFile in projectFiles)
        {
            var folder = Path.GetDirectoryName(projectFile)!;
            var name = Path.GetFileNameWithoutExtension(projectFile);
            var projectId = ProjectId.CreateNewId(name);
            solution = solution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), name, name, LanguageNames.CSharp,
                filePath: projectFile,
                parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: references));
            byPath[projectFile] = projectId;
            foreach (var path in SourceFiles(folder))
            {
                solution = solution.AddDocument(DocumentId.CreateNewId(projectId), Path.GetFileNameWithoutExtension(path),
                    SourceText.From(File.ReadAllText(path)), filePath: path);
            }
        }

        foreach (var projectFile in projectFiles)
        {
            foreach (var target in ProjectReferences(projectFile, byPath))
            {
                solution = solution.AddProjectReference(byPath[projectFile], new ProjectReference(target));
            }
        }

        if (!workspace.TryApplyChanges(solution))
        {
            throw new InvalidOperationException("Could not attach the sample projects.");
        }

        return Task.FromResult(new LoadedSolution(workspace, []));
    }

    private static IEnumerable<string> SourceFiles(string folder)
        => Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal);

    private static IEnumerable<ProjectId> ProjectReferences(string projectFile, IReadOnlyDictionary<string, ProjectId> byPath)
    {
        var folder = Path.GetDirectoryName(projectFile)!;
        var include = System.Xml.Linq.XDocument.Load(projectFile)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(Path.Combine(folder, value!.Replace('\\', Path.DirectorySeparatorChar))))
            .Where(byPath.ContainsKey);
        return include.Select(path => byPath[path]);
    }
}
