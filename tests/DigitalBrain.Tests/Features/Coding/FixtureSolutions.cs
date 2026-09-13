using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DigitalBrain.Tests.Coding;

// Two projects: Alpha declares Greeter, Beta references Alpha and calls it; Broken.cs has one error.
internal static class FixtureSolutions
{
    internal const string Root = "E:/fixture";
    internal const string GreeterPath = Root + "/Alpha/Greeter.cs";
    internal const string ProgramPath = Root + "/Beta/Program.cs";
    internal const string BrokenPath = Root + "/Beta/Broken.cs";

    private static readonly IReadOnlyList<MetadataReference> Runtime = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(static path => Path.GetFileName(path) is "System.Runtime.dll" or "System.Private.CoreLib.dll" or "netstandard.dll" or "System.Console.dll")
        .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToArray();

    internal static Workspace TwoProjects()
    {
        var workspace = new AdhocWorkspace();
        var alpha = ProjectId.CreateNewId("Alpha");
        var beta = ProjectId.CreateNewId("Beta");
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(alpha, VersionStamp.Create(), "Alpha", "Alpha", LanguageNames.CSharp,
                filePath: Root + "/Alpha/Alpha.csproj", compilationOptions: options, metadataReferences: Runtime))
            .AddProject(ProjectInfo.Create(beta, VersionStamp.Create(), "Beta", "Beta", LanguageNames.CSharp,
                filePath: Root + "/Beta/Beta.csproj", compilationOptions: options, metadataReferences: Runtime,
                projectReferences: [new ProjectReference(alpha)]))
            .AddDocument(DocumentId.CreateNewId(alpha), "Greeter.cs", SourceText.From("""
                namespace Alpha;

                public sealed class Greeter
                {
                    public string Greet(string name) => $"Hello, {name}";
                }
                """), filePath: GreeterPath)
            .AddDocument(DocumentId.CreateNewId(beta), "Program.cs", SourceText.From("""
                using Alpha;

                namespace Beta;

                public static class Program
                {
                    public static string Run() => new Greeter().Greet("world");
                }
                """), filePath: ProgramPath)
            .AddDocument(DocumentId.CreateNewId(beta), "Broken.cs", SourceText.From("""
                namespace Beta;

                public static class Broken
                {
                    public static int Count() => "not a number";
                }
                """), filePath: BrokenPath);
        if (!workspace.TryApplyChanges(solution))
        {
            throw new InvalidOperationException("The adhoc fixture did not apply.");
        }

        return workspace;
    }
}
