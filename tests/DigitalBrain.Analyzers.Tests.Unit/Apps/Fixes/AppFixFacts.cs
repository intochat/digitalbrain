using System.Collections.Immutable;
using DigitalBrain.Analyzers.Apps;
using DigitalBrain.Analyzers.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DigitalBrain.Analyzers.Tests.Unit.Apps.Fixes;

// Code-fix facts without the CodeFix.Testing package (not pinned): an AdhocWorkspace with the Verify stubs, the
// analyzer run through CompilationWithAnalyzers, and the fix applied (single fix or BatchFixer fix-all).
internal static class FixRunner
{
    public static async Task<(string Fixed, ImmutableArray<Diagnostic> Before, ImmutableArray<Diagnostic> After)> Apply(
        DiagnosticAnalyzer analyzer, CodeFixProvider fixer, string source, bool fixAll = false, string? key = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var references = await Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, ct);
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "Fix", "Fix", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: references));
        project = project.AddDocument("Stubs.cs", SourceText.From(Verify<AppBaseAnalyzer>.Stubs)).Project;
        var document = project.AddDocument("Test.cs", SourceText.From(source));

        var before = await Diagnose(document, analyzer, ct);
        Assert.NotEmpty(before);
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, before[0], (action, _) => actions.Add(action), ct);
        await fixer.RegisterCodeFixesAsync(context);
        var first = key is null ? Assert.Single(actions) : Assert.Single(actions, candidate => candidate.EquivalenceKey == key);

        CodeAction action = first;
        if (fixAll)
        {
            var provider = new Provider(document, analyzer);
            var all = new FixAllContext(document, fixer, FixAllScope.Document, first.EquivalenceKey, fixer.FixableDiagnosticIds, provider, ct);
            action = (await fixer.GetFixAllProvider()!.GetFixAsync(all))!;
        }
        var operations = await action.GetOperationsAsync(ct);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = solution.GetDocument(document.Id)!;
        var text = (await changed.GetTextAsync(ct)).ToString();
        var after = await Diagnose(changed, analyzer, ct);
        var compile = (await changed.Project.GetCompilationAsync(ct))!.GetDiagnostics(ct).Where(d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(compile);
        return (text, before, after);
    }

    // The equivalence keys of the actions offered for the first diagnostic.
    public static async Task<IReadOnlyList<string?>> Offered(DiagnosticAnalyzer analyzer, CodeFixProvider fixer, string source)
    {
        var ct = TestContext.Current.CancellationToken;
        var references = await Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, ct);
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "Fix", "Fix", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: references));
        project = project.AddDocument("Stubs.cs", SourceText.From(Verify<AppBaseAnalyzer>.Stubs)).Project;
        var document = project.AddDocument("Test.cs", SourceText.From(source));
        var before = await Diagnose(document, analyzer, ct);
        Assert.NotEmpty(before);
        var actions = new List<CodeAction>();
        await fixer.RegisterCodeFixesAsync(new CodeFixContext(document, before[0], (action, _) => actions.Add(action), ct));
        return [.. actions.Select(action => action.EquivalenceKey)];
    }

    private static async Task<ImmutableArray<Diagnostic>> Diagnose(Document document, DiagnosticAnalyzer analyzer, CancellationToken ct)
    {
        var compilation = (await document.Project.GetCompilationAsync(ct))!;
        var tree = await document.GetSyntaxTreeAsync(ct);
        var all = await compilation.WithAnalyzers([analyzer], document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync(ct);
        return [.. all.Where(d => d.Location.SourceTree == tree).OrderBy(d => d.Location.SourceSpan.Start)];
    }

    private sealed class Provider(Document document, DiagnosticAnalyzer analyzer) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
            => GetDocumentDiagnosticsAsync(document, cancellationToken);

        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document doc, CancellationToken cancellationToken)
            => await Diagnose(doc, analyzer, cancellationToken);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
            => Task.FromResult(Enumerable.Empty<Diagnostic>());
    }

    public static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
}

public sealed class AppBaseFixFacts
{
    [Fact]
    public async Task Adds_the_App_base_to_an_App_class_without_one()
    {
        var (text, _, after) = await FixRunner.Apply(new AppBaseAnalyzer(), new AppBaseFix(), """
            using DigitalBrain;
            [App("a")] public sealed class A { }
            """);
        Assert.Equal("""
            using DigitalBrain;
            [App("a")] public sealed class A : App { }
            """, FixRunner.Normalize(text));
        Assert.Empty(after);
    }

    [Fact]
    public async Task Replaces_a_wrong_class_base_and_keeps_interfaces()
    {
        var (text, _, after) = await FixRunner.Apply(new AppBaseAnalyzer(), new AppBaseFix(), """
            using System;
            using DigitalBrain;
            public abstract class NotApp { }
            [App("a")] public sealed class A : NotApp, IDisposable { public void Dispose() { } }
            """);
        Assert.Contains("public sealed class A : App, IDisposable", text, StringComparison.Ordinal);
        Assert.Empty(after);
    }
}

public sealed class EmptyCatchFixFacts
{
    [Fact]
    public async Task Rethrow_replaces_the_swallowing_body()
    {
        var (text, _, after) = await FixRunner.Apply(new EmptyCatchAnalyzer(), new EmptyCatchFix(), key: EmptyCatchFix.Key, source: """
            using System;
            using DigitalBrain;
            public sealed class A : App
            {
                public void Run()
                {
                    try { Console.WriteLine(); }
                    catch (Exception) { return; }
                }
            }
            """);
        Assert.Contains("throw;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("return;", text, StringComparison.Ordinal);
        Assert.Empty(after);
    }

    [Fact]
    public async Task Fix_all_rewrites_every_site_in_the_document()
    {
        var (text, before, after) = await FixRunner.Apply(new EmptyCatchAnalyzer(), new EmptyCatchFix(), key: EmptyCatchFix.Key, source: """
            using System;
            using DigitalBrain;
            public sealed class A : App
            {
                public void One() { try { } catch { } }
                public void Two() { try { } catch (Exception) { } }
                public void Three() { foreach (var i in new int[1]) { try { } catch { continue; } } }
            }
            """, fixAll: true);
        Assert.Equal(3, before.Length);
        Assert.Equal(3, text.Split("throw;").Length - 1);
        Assert.Empty(after);
    }
}

public sealed class TypedRecordFixFacts
{
    [Fact]
    public async Task Anonymous_payload_becomes_a_nested_record()
    {
        var (text, _, after) = await FixRunner.Apply(new UntypedJsonAnalyzer(), new TypedRecordFix(), """
            using System.Text.Json;
            using DigitalBrain;
            public sealed class A : App
            {
                public string Save(int count, string name) => JsonSerializer.Serialize(new { Count = count, name });
            }
            """);
        Assert.Contains("JsonSerializer.Serialize(new SavePayload(count, name))", text, StringComparison.Ordinal);
        Assert.Contains("private sealed record SavePayload(int Count, string name);", text, StringComparison.Ordinal);
        Assert.Empty(after);
    }

    [Fact]
    public async Task Json_nodes_get_no_mechanical_fix()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAnyAsync<Exception>(() => FixRunner.Apply(new UntypedJsonAnalyzer(), new TypedRecordFix(), """
            using System.Text.Json.Nodes;
            using DigitalBrain;
            public sealed class A : App { public object? Read(string s) => JsonNode.Parse(s); }
            """));
        Assert.False(ct.IsCancellationRequested);
    }
}

public sealed class SpecCommentFixFacts
{
    [Fact]
    public async Task Inserts_a_parseable_stub_with_role_optional_shipped_false_and_the_operations()
    {
        var (text, _, after) = await FixRunner.Apply(new SpecCommentAnalyzer(), new SpecCommentFix(), """
            using System.Threading.Tasks;
            using DigitalBrain;
            public sealed record Opened : Signal;
            [App("notes")] public sealed class Notes : App
            {
                [On] public Task Open(Opened s) => Task.CompletedTask;
                [On] public Task ClearAll(Opened s) => Task.CompletedTask;
            }
            """);
        Assert.StartsWith("/*\n# Notes\n\nRole: Optional\nShipped: false\nOperations: open, clearall\n", FixRunner.Normalize(text), StringComparison.Ordinal);
        Assert.Empty(after);

        var spec = DigitalBrain.Apps.SpecComment.Parse(DigitalBrain.Apps.SpecComment.Extract(text)!);
        Assert.Empty(spec.Errors);
        Assert.Equal(DigitalBrain.Apps.AppRole.Optional, spec.Role);
        Assert.False(spec.Shipped);
        Assert.True(spec.Describes("Open"));
        Assert.True(spec.Describes("ClearAll"));
    }
}
