using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DigitalBrain.Analyzers.Screens;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace DigitalBrain.Analyzers.Tests.Unit.Screens;

public sealed class ScreenRulesFacts
{
    [Fact]
    public void All_rules_have_correct_ids_category_and_severities()
    {
        Assert.Equal(["DB0022", "DB0025", "DB0027", "DB0028"], ScreenRules.All.Select(d => d.Id));
        Assert.All(ScreenRules.All, d => Assert.Equal("DigitalBrain.Screens", d.Category));
        Assert.Same(ScreenRules.Suppression, ScreenRules.ById("DB0028"));
        Assert.Null(ScreenRules.ById("DB0002"));
    }

    [Fact]
    public void All_screen_rules_are_unshipped()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DigitalBrain.Analyzers", "AnalyzerReleases.Unshipped.md");
        Assert.True(File.Exists(path), "Missing " + path);
        var text = File.ReadAllText(path);
        Assert.All(ScreenRules.All, d => Assert.Contains(d.Id + " | DigitalBrain.Screens | " + d.DefaultSeverity, text, StringComparison.Ordinal));
    }
}

public sealed class AnalyzerSetFacts
{
    [Fact]
    public void All_holds_ten_analyzers_covering_every_rule_once()
    {
        Assert.Equal(10, AnalyzerSet.All.Length);
        var ids = AnalyzerSet.All.SelectMany(a => a.SupportedDiagnostics).Select(d => d.Id).ToArray();
        Assert.Equal(
            ["DB0001", "DB0003", "DB0013", "DB0022", "DB0025", "DB0027", "DB0028", "DB0031", "DB0032", "DB0033"],
            ids.Order(StringComparer.Ordinal));
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_screen_rule_has_an_analyzer_in_the_set()
    {
        var ids = AnalyzerSet.All.SelectMany(a => a.SupportedDiagnostics).Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        Assert.All(ScreenRules.All, d => Assert.Contains(d.Id, ids));
        Assert.True(typeof(ScreenRules).IsPublic);
        Assert.True(typeof(AnalyzerSet).IsPublic);
    }
}

public sealed class EmptyCatchAnalyzerFacts
{
    [Fact]
    public Task Empty_catch_in_screen_reports_DB0022() => Verify<EmptyCatchAnalyzer>.Diagnostics("""
        using System;
        using DigitalBrain;
        using DigitalBrain.UI;
        public sealed class SState { }
        public sealed class S : Screen<SState>
        {
            protected override View Render(SState state) => new("S", 1);
            public void Swallow() { try { Console.WriteLine(); } {|DB0022:catch|} { } }
            public void Return() { try { Console.WriteLine(); } {|DB0022:catch|} (Exception) { return; } }
            public void NonSwallow() { try { Console.WriteLine(); } catch (Exception e) { throw; } }
        }
        """);

    [Fact]
    public Task Empty_catch_in_non_screen_reports_nothing() => Verify<EmptyCatchAnalyzer>.Diagnostics("""
        using System;
        public sealed class Helper
        {
            public void Swallow() { try { Console.WriteLine(); } catch { } }
        }
        """);
}

public sealed class UntypedJsonAnalyzerFacts
{
    [Fact]
    public Task Hand_written_json_in_screen_reports_DB0025() => Verify<UntypedJsonAnalyzer>.Diagnostics("""
        using System.Text.Json;
        using System.Text.Json.Nodes;
        using DigitalBrain;
        using DigitalBrain.UI;
        public sealed class SState { }
        public sealed class S : Screen<SState>
        {
            protected override View Render(SState state) => new("S", 1);
            public string Anonymous(int n) => {|DB0025:JsonSerializer.Serialize(new { Count = n })|};
            public string Boxed(object o) => {|DB0025:JsonSerializer.Serialize(o)|};
            public string? Node(string s) => {|DB0025:JsonNode.Parse(s)|}?["a"]?.ToString();
            public JsonObject Created() => {|DB0025:new JsonObject()|};
            public int Document(string s) { using var d = {|DB0025:JsonDocument.Parse(s)|}; return 1; }
            public string Built(string v) => {|DB0025:"{\"name\":\"" + v + "\"}"|};
            public string Interpolated(string v) => {|DB0025:$"{{\"name\":\"{v}\"}}"|};
        }
        """);

    [Fact]
    public Task Typed_records_in_screen_report_nothing() => Verify<UntypedJsonAnalyzer>.Diagnostics("""
        using System.Text.Json;
        using DigitalBrain;
        using DigitalBrain.UI;
        public sealed record Payload(int Count);
        public sealed class SState { }
        public sealed class S : Screen<SState>
        {
            protected override View Render(SState state) => new("S", 1);
            public string Typed(int n) => JsonSerializer.Serialize(new Payload(n));
            public Payload? Back(string s) => JsonSerializer.Deserialize<Payload>(s);
            public string Plain(string v) => "name: " + v;
        }
        public sealed class Helper { public string? Node(string s) => System.Text.Json.Nodes.JsonNode.Parse(s)?.ToString(); }
        """);
}

public sealed class SpecCommentAnalyzerFacts
{
    [Fact]
    public Task Screen_without_spec_comment_reports_DB0027() => Verify<SpecCommentAnalyzer>.Diagnostics("""
        using DigitalBrain;
        using DigitalBrain.UI;
        public sealed class SState { }
        public sealed class {|DB0027:S|} : Screen<SState>
        {
            protected override View Render(SState state) => new("S", 1);
        }
        """);

    [Fact]
    public Task Screen_with_leading_spec_comment_reports_nothing() => Verify<SpecCommentAnalyzer>.Diagnostics("""
        /*
        # Calculator
        Role: Example
        Shipped: true
        A calculator.
        */
        using DigitalBrain;
        using DigitalBrain.UI;
        public sealed class CalculatorState { }
        public sealed class Calculator : Screen<CalculatorState>
        {
            protected override View Render(CalculatorState state) => new("Calculator", 1);
        }
        """);
}

public sealed class SuppressionAnalyzerFacts
{
    private static Task Run(string source)
    {
        var test = Verify<SuppressionAnalyzer>.Create(source);
        test.TestBehaviors |= TestBehaviors.SkipSuppressionCheck;
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task Suppression_in_screen_file_reports_DB0028() => Run("""
        using System.Diagnostics.CodeAnalysis;
        using DigitalBrain;
        using DigitalBrain.UI;
        public sealed class SState { }
        public sealed class S : Screen<SState>
        {
            protected override View Render(SState state) => new("S", 1);
            {|DB0028:#pragma warning disable DB0022|}
            public void M() { }
            {|DB0028:#pragma warning restore DB0022|}
            [{|DB0028:SuppressMessage("DigitalBrain.Screens", "DB0022")|}] public void M1() { }
        }
        """);

    [Fact]
    public Task Suppression_in_non_screen_file_reports_nothing() => Run("""
        using System.Diagnostics.CodeAnalysis;
        public sealed class Helper
        {
            #pragma warning disable DB0022
            public void M() { }
            #pragma warning restore DB0022
            [SuppressMessage("DigitalBrain.Screens", "DB0022")] public void M1() { }
        }
        """);
}
