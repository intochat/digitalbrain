using DigitalBrain.Coding;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ChangeSetFacts
{
    private const string Sample = "namespace Sample;\n\npublic class C\n{\n    public void M() { }\n}\n";

    [Fact]
    public async Task ReplaceRangeEditsTheNamedLines()
    {
        var ct = TestContext.Current.CancellationToken;
        var solution = NewSolution();
        var edits = new[] { new EditRequest(EditKind.ReplaceRange, Path: "Sample.cs", Source: "    public void M() { System.Console.WriteLine(1); }", StartLine: 5, EndLine: 5) };
        var outcome = await NewEditor().ApplyAsync(solution, edits, ct);
        Assert.False(outcome.HasErrors);
        Assert.Contains("WriteLine", outcome.Diff);
        Assert.Equal(["Sample.cs"], outcome.ChangedPaths);
    }

    [Fact]
    public async Task AddUsingImportsTheNamespace()
    {
        var ct = TestContext.Current.CancellationToken;
        var outcome = await NewEditor().ApplyAsync(NewSolution(), [new EditRequest(EditKind.AddUsing, Path: "Sample.cs", Namespace: "System.Text")], ct);
        Assert.False(outcome.HasErrors);
        Assert.Contains("using System.Text;", outcome.Diff);
    }

    [Fact]
    public async Task ReplaceMemberReplacesTheDeclaration()
    {
        var ct = TestContext.Current.CancellationToken;
        var outcome = await NewEditor().ApplyAsync(NewSolution(), [new EditRequest(EditKind.ReplaceMember, SymbolId: "M:Sample.C.M", Source: "public void N() { }")], ct);
        Assert.False(outcome.HasErrors);
        Assert.Contains("public void N()", outcome.Diff);
    }

    [Fact]
    public async Task RenameRewritesTheSymbol()
    {
        var ct = TestContext.Current.CancellationToken;
        var outcome = await NewEditor().ApplyAsync(NewSolution(), [new EditRequest(EditKind.Rename, SymbolId: "T:Sample.C", NewName: "D")], ct);
        Assert.False(outcome.HasErrors);
        Assert.Contains("class D", outcome.Diff);
    }

    [Fact]
    public async Task AnOutOfRangeEditSettlesAsAdvice()
    {
        var ct = TestContext.Current.CancellationToken;
        var outcome = await NewEditor().ApplyAsync(NewSolution(), [new EditRequest(EditKind.ReplaceRange, Path: "Sample.cs", Source: "x", StartLine: 99, EndLine: 99)], ct);
        Assert.True(outcome.HasErrors);
        Assert.Contains("outside", outcome.Detail);
    }

    [Fact]
    public async Task NoEditsProduceNoChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var outcome = await NewEditor().ApplyAsync(NewSolution(), [], ct);
        Assert.Equal(string.Empty, outcome.Diff);
        Assert.Empty(outcome.ChangedPaths);
    }

    [Fact]
    public async Task TheNeuronAppendsProposalsAndPublishes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<CodingModule>()
            .StartAsync(ct);
        var changeSet = brain.Get<IChangeSet>("change-1");
        await using var changed = await brain.Observe<ChangeSetChanged>(changeSet, ct);
        var receipt = await changeSet.Propose(new ProposeEdit(new EditRequest(EditKind.ReplaceRange, Path: "Sample.cs", Source: "x", StartLine: 1, EndLine: 1)));
        Assert.Equal(1, receipt.EditCount);
        Assert.Equal(ChangeSetStatus.Draft, receipt.Status);
        var published = await changed.NextAsync(ct: ct);
        Assert.Equal("change-1", published.ChangeId);
        Assert.Equal(1, published.EditCount);
        Assert.Equal(1, published.Revision);
    }

    private static ChangeSetEditor NewEditor() => new(new CodeFixCatalog());

    private static Solution NewSolution()
    {
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
        return workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "Sample", "Sample", LanguageNames.CSharp,
                parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: references))
            .AddDocument(documentId, "Sample.cs", SourceText.From(Sample), filePath: "Sample.cs");
    }
}
