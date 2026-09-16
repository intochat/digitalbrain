using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
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

    // Stands in for Orleans/Roslyn source-generator output: a declaration that mentions Greeter, sitting
    // under obj/, so query-hygiene facts can prove it is dropped from find-symbols and marked in references.
    internal const string GeneratedGreeterPath = Root + "/Alpha/obj/Debug/net11.0/Greeter.g.cs";

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
            private const string Suffix = "!";

            public string Shout(string name) => name.ToUpperInvariant() + Suffix;
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

    internal const string GeneratedGreeterSource = """
        namespace Alpha;

        public sealed class GreeterCodec
        {
            public static string Describe(Greeter greeter) => greeter.Greet("codec");
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
        (root + "/Alpha/obj/Debug/net11.0/Greeter.g.cs", GeneratedGreeterSource),
    ];

    internal static Workspace TwoProjects() => Build(Root);

    internal static Workspace Build(string root)
    {
        var workspace = new DiskWritingWorkspace(root);
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

    // AdhocWorkspace is sealed, so this mirrors it directly: same permissive CanApplyChange, but
    // ApplyDocumentTextChanged also writes to disk, like MSBuildWorkspace does against the live kernel.
    // The adhoc Root fixture ("E:/fixture") never had its paths created on disk, so it is named explicitly
    // and the write is skipped for it; only a DiskFixture-backed workspace (real temp files) actually gets
    // touched. Unlike MSBuildWorkspace's own unconditional write, this one checks content first:
    // Solution.WithDocumentText bumps the document's version even when the new text is byte-identical to
    // what is already there, and TryApplyChanges still routes that through ApplyDocumentTextChanged -- an
    // unguarded write would then touch the file's mtime on a semantic no-op and break
    // Commit_of_unchanged_text_writes_nothing.
    private sealed class DiskWritingWorkspace(string root) : Workspace(MefHostServices.DefaultHost, WorkspaceKind.Host)
    {
        public override bool CanApplyChange(ApplyChangesKind feature) => true;

        protected override void ApplyDocumentTextChanged(DocumentId documentId, SourceText newText)
        {
            if (root != Root)
            {
                var path = CurrentSolution.GetDocument(documentId)?.FilePath;
                if (path is not null)
                {
                    var content = newText.ToString();
                    if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
                    {
                        File.WriteAllText(path, content);
                    }
                }
            }

            base.ApplyDocumentTextChanged(documentId, newText);
        }
    }
}
