using DigitalBrain.Coding;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class ChangeSetEditorFacts
{
    private static readonly ChangeSetEditor Editor = new();

    private static Solution Snapshot() => FixtureSolutions.TwoProjects().CurrentSolution;

    private static async Task<string> TextAsync(Solution solution, string path)
        => (await SolutionQueries.DocumentAt(solution, path).GetTextAsync(TestContext.Current.CancellationToken)).ToString();

    [Fact]
    public async Task Replacing_a_member_yields_a_clean_snapshot_and_a_diff()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.ReplaceMember, SymbolId: "M:Alpha.Greeter.Greet(System.String)", Source: """public string Greet(string name) => $"Hi, {name}";""")],
            TestContext.Current.CancellationToken);
        Assert.False(outcome.HasErrors);
        Assert.Null(outcome.FailingEdit);
        Assert.Equal([FixtureSolutions.GreeterPath], outcome.ChangedPaths);
        Assert.Contains("""-    public string Greet(string name) => $"Hello, {name}";""", outcome.Diff, StringComparison.Ordinal);
        Assert.Contains("""+    public string Greet(string name) => $"Hi, {name}";""", outcome.Diff, StringComparison.Ordinal);
        Assert.Contains("Hi, {name}", await TextAsync(outcome.Changed, FixtureSolutions.GreeterPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_member_that_does_not_compile_names_the_responsible_edit()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.ReplaceMember, SymbolId: "M:Alpha.Greeter.Greet(System.String)", Source: "public string Greet(string name) => 42;")],
            TestContext.Current.CancellationToken);
        Assert.True(outcome.HasErrors);
        Assert.Equal(0, outcome.FailingEdit);
        Assert.StartsWith("edit 1 (ReplaceMember M:Alpha.Greeter.Greet(System.String))", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains(outcome.Diagnostics, hit => hit.Id == "CS0029" && hit.Path == FixtureSolutions.GreeterPath);
    }

    [Fact]
    public async Task Dependent_projects_are_diagnosed_too()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.ReplaceMember, SymbolId: "M:Alpha.Greeter.Greet(System.String)", Source: "public string Greet(int count) => count.ToString();")],
            TestContext.Current.CancellationToken);
        Assert.True(outcome.HasErrors);
        Assert.Contains(outcome.Diagnostics, hit => hit.Path == FixtureSolutions.ProgramPath && hit.Severity == "Error");
        Assert.Equal(0, outcome.FailingEdit);
    }

    [Fact]
    public async Task Inserting_into_a_type_appends_a_member()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.InsertMember, SymbolId: "T:Alpha.Greeter", Source: "public int Count => 1;")],
            TestContext.Current.CancellationToken);
        Assert.False(outcome.HasErrors);
        var skeleton = await SolutionQueries.SkeletonAsync(outcome.Changed, new(FixtureSolutions.GreeterPath), TestContext.Current.CancellationToken);
        Assert.Equal(["T:Alpha.Greeter", "M:Alpha.Greeter.Greet(System.String)", "M:Alpha.Greeter.Welcome(System.String)", "P:Alpha.Greeter.Count"], skeleton.Members.Select(member => member.Id));
    }

    [Fact]
    public async Task Inserting_after_a_member_keeps_the_order()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.InsertMember, SymbolId: "M:Alpha.Greeter.Greet(System.String)", Source: "public int Count => 1;")],
            TestContext.Current.CancellationToken);
        Assert.False(outcome.HasErrors);
        var skeleton = await SolutionQueries.SkeletonAsync(outcome.Changed, new(FixtureSolutions.GreeterPath), TestContext.Current.CancellationToken);
        Assert.Equal(["T:Alpha.Greeter", "M:Alpha.Greeter.Greet(System.String)", "P:Alpha.Greeter.Count", "M:Alpha.Greeter.Welcome(System.String)"], skeleton.Members.Select(member => member.Id));
    }

    [Fact]
    public async Task Adding_a_using_is_idempotent()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.AddUsing, Path: FixtureSolutions.GreeterPath, Namespace: "System.Text"),
             new EditRequest(EditKind.AddUsing, Path: FixtureSolutions.GreeterPath, Namespace: "System.Text")],
            TestContext.Current.CancellationToken);
        Assert.False(outcome.HasErrors);
        var text = await TextAsync(outcome.Changed, FixtureSolutions.GreeterPath);
        Assert.StartsWith("using System.Text;", text, StringComparison.Ordinal);
        Assert.Equal(1, text.Split("using System.Text;").Length - 1);
    }

    [Fact]
    public async Task Replacing_a_line_range_is_a_text_edit()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.ReplaceRange, Path: FixtureSolutions.ProgramPath, StartLine: 7, EndLine: 7,
                Source: """    public static string Run() => new Greeter().Welcome("world");""")],
            TestContext.Current.CancellationToken);
        Assert.False(outcome.HasErrors);
        Assert.Contains(""".Welcome("world")""", await TextAsync(outcome.Changed, FixtureSolutions.ProgramPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_symbol_fails_that_edit_with_advice()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.AddUsing, Path: FixtureSolutions.GreeterPath, Namespace: "System.Text"),
             new EditRequest(EditKind.ReplaceMember, SymbolId: "M:Nowhere.Missing", Source: "public int X => 1;")],
            TestContext.Current.CancellationToken);
        Assert.True(outcome.HasErrors);
        Assert.Equal(1, outcome.FailingEdit);
        Assert.Contains("find-symbols", outcome.Detail, StringComparison.Ordinal);
        Assert.Empty(outcome.Diagnostics);
    }

    [Fact]
    public async Task A_source_that_is_not_a_member_is_refused()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.ReplaceMember, SymbolId: "M:Alpha.Greeter.Greet(System.String)", Source: "this is not C#")],
            TestContext.Current.CancellationToken);
        Assert.Equal(0, outcome.FailingEdit);
        Assert.Contains("member declaration", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pre_existing_diagnostics_that_merely_shift_are_not_introduced()
    {
        var outcome = await Editor.ApplyAsync(Snapshot(),
            [new EditRequest(EditKind.ReplaceRange, Path: FixtureSolutions.UnusedPath, StartLine: 1, EndLine: 1,
                Source: "namespace Beta;\n\n// a comment above the class")],
            TestContext.Current.CancellationToken);
        Assert.False(outcome.HasErrors);
        Assert.Empty(outcome.Diagnostics);
    }
}
