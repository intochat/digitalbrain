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
    internal const string WelcomePath = Root + "/Alpha/IWelcome.cs";
    internal const string ShouterPath = Root + "/Beta/Shouter.cs";
    internal const string UnusedPath = Root + "/Beta/Unused.cs";

    private static readonly IReadOnlyList<MetadataReference> Runtime = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(static path => Path.GetFileName(path) is "System.Runtime.dll" or "System.Private.CoreLib.dll" or "netstandard.dll" or "System.Console.dll")
        .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToArray();

    internal const string GreeterSource = """
        namespace Alpha;

        public class Greeter : IWelcome
        {
            public string Greet(string name) => $"Hello, {name}";

            public string Welcome(string name) => "Welcome, " + name;
        }
        """;

    internal const string WelcomeSource = """
        namespace Alpha;

        public interface IWelcome
        {
            string Welcome(string name);
        }
        """;

    internal const string ProgramSource = """
        using Alpha;

        namespace Beta;

        public static class Program
        {
            public static string Run() => new Greeter().Greet("world");
        }
        """;

    internal const string BrokenSource = """
        namespace Beta;

        public static class Broken
        {
            public static int Count() => "not a number";
        }
        """;

    internal const string ShouterSource = """
        using Alpha;

        namespace Beta;

        public sealed class Shouter : Greeter
        {
            public string Shout(string name) => name.ToUpperInvariant();
        }
        """;

    internal const string UnusedSource = """
        namespace Beta;

        public static class Unused
        {
            public static void Run()
            {
                int count = 1;
            }
        }
        """;

    internal static IReadOnlyList<(string Path, string Source)> Documents(string root) =>
    [
        (root + "/Alpha/Greeter.cs", GreeterSource),
        (root + "/Alpha/IWelcome.cs", WelcomeSource),
        (root + "/Beta/Program.cs", ProgramSource),
        (root + "/Beta/Broken.cs", BrokenSource),
        (root + "/Beta/Shouter.cs", ShouterSource),
        (root + "/Beta/Unused.cs", UnusedSource),
    ];

    internal static Workspace TwoProjects() => Build(Root);

    internal static Workspace Build(string root)
    {
        var workspace = new AdhocWorkspace();
        var alpha = ProjectId.CreateNewId("Alpha");
        var beta = ProjectId.CreateNewId("Beta");
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(alpha, VersionStamp.Create(), "Alpha", "Alpha", LanguageNames.CSharp,
                filePath: root + "/Alpha/Alpha.csproj", compilationOptions: options, metadataReferences: Runtime))
            .AddProject(ProjectInfo.Create(beta, VersionStamp.Create(), "Beta", "Beta", LanguageNames.CSharp,
                filePath: root + "/Beta/Beta.csproj", compilationOptions: options, metadataReferences: Runtime,
                projectReferences: [new ProjectReference(alpha)]));
        foreach (var (path, source) in Documents(root))
        {
            var project = path.Contains("/Alpha/", StringComparison.Ordinal) ? alpha : beta;
            solution = solution.AddDocument(DocumentId.CreateNewId(project), System.IO.Path.GetFileName(path), SourceText.From(source), filePath: path);
        }

        if (!workspace.TryApplyChanges(solution))
        {
            throw new InvalidOperationException("The adhoc fixture did not apply.");
        }

        return workspace;
    }

    // A console project with no entry point: the missing Main is a compilation-level diagnostic (Location.None).
    internal static Workspace ConsoleWithoutMain()
    {
        var workspace = new AdhocWorkspace();
        var gamma = ProjectId.CreateNewId("Gamma");
        var options = new CSharpCompilationOptions(OutputKind.ConsoleApplication);
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(gamma, VersionStamp.Create(), "Gamma", "Gamma", LanguageNames.CSharp,
                filePath: Root + "/Gamma/Gamma.csproj", compilationOptions: options, metadataReferences: Runtime))
            .AddDocument(DocumentId.CreateNewId(gamma), "Empty.cs", SourceText.From("""
                namespace Gamma;
                """), filePath: Root + "/Gamma/Empty.cs");
        if (!workspace.TryApplyChanges(solution))
        {
            throw new InvalidOperationException("The adhoc fixture did not apply.");
        }

        return workspace;
    }
}
