using DigitalBrain.Analyzers.Apps;
using DigitalBrain.Analyzers.Reactions;
using DigitalBrain.Apps;

namespace DigitalBrain.Analyzers.Tests.Unit.Apps;

public sealed class SpecCommentParserFacts
{
    private const string Calculator = """
        # Calculator

        Role: Example
        Shipped: true

        A keypad that evaluates arithmetic.

        ## Scenario: Opening shows the keypad

        Invoking "open" presents the keypad.

        ## Scenario: Clear from the dock

        The toolbar posts "clear"; the display becomes empty.

        ## Scenario: Add answers a sum

        Invoking "add" with "2+3" sets the sum to "5".
        """;

    [Fact]
    public void Parses_title_role_shipped_description_scenarios_and_invoked_operations()
    {
        var spec = SpecComment.Parse(Calculator);
        Assert.Empty(spec.Errors);
        Assert.Equal("Calculator", spec.Title);
        Assert.Equal(AppRole.Example, spec.Role);
        Assert.True(spec.Shipped);
        Assert.Equal("A keypad that evaluates arithmetic.", spec.Description);
        Assert.Equal(["Opening shows the keypad", "Clear from the dock", "Add answers a sum"], spec.Scenarios);
        Assert.False(spec.OperationsDeclared);
        Assert.Equal(["add", "clear", "open"], spec.Operations.Order(StringComparer.Ordinal));
        Assert.True(spec.Describes("Open"));
        Assert.True(spec.Describes("Clear"));
        Assert.False(spec.Describes("Multiply"));
    }

    [Theory]
    [InlineData("Shell", (int)AppRole.Shell)]
    [InlineData("Default", (int)AppRole.Default)]
    [InlineData("Optional", (int)AppRole.Optional)]
    [InlineData("Example", (int)AppRole.Example)]
    public void Every_role_parses(string text, int role)
    {
        var spec = SpecComment.Parse("# A\n\nRole: " + text + "\nShipped: false\n");
        Assert.Empty(spec.Errors);
        Assert.Equal((AppRole)role, spec.Role);
        Assert.False(spec.Shipped);
    }

    [Theory]
    [InlineData("Role: Admin\n", 2, "Role is not one of")]
    [InlineData("Role: example\n", 2, "Role is not one of")]
    [InlineData("Role: Shell\nRole: Default\n", 3, "Role is declared twice")]
    [InlineData("Shipped: yes\n", 2, "Shipped is not true or false")]
    public void Unknown_or_repeated_headers_are_errors_naming_the_line_never_defaults(string headers, int line, string message)
    {
        var spec = SpecComment.Parse("# A\n" + headers);
        var error = Assert.Single(spec.Errors);
        Assert.Equal(line, error.Line);
        Assert.StartsWith(message, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Admin", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Absent_headers_are_null_without_errors()
    {
        var spec = SpecComment.Parse("# A\n\nJust prose.");
        Assert.Null(spec.Role);
        Assert.Null(spec.Shipped);
        Assert.Empty(spec.Errors);
    }

    [Fact]
    public void Operations_header_is_explicit_and_normalized()
    {
        var spec = SpecComment.Parse("# A\nOperations: open, Add-Item, `clear_all`\n\nInvoking \"other\" is prose here.");
        Assert.True(spec.OperationsDeclared);
        Assert.Equal(["open", "additem", "clearall"], spec.Operations);
        Assert.True(spec.Describes("AddItem"));
        Assert.False(spec.Describes("other"));
    }

    [Fact]
    public void Headers_inside_sections_are_prose()
    {
        var spec = SpecComment.Parse("# A\n\n## Scenario: x\n\nRole: Admin\n");
        Assert.Null(spec.Role);
        Assert.Empty(spec.Errors);
    }

    [Fact]
    public void Text_over_64_KiB_is_cut_and_reported()
    {
        var spec = SpecComment.Parse("# A\nRole: Shell\n" + new string('x', SpecComment.MaxLength + 10));
        Assert.Equal(SpecComment.MaxLength, spec.Text.Length);
        Assert.Equal(AppRole.Shell, spec.Role);
        Assert.Contains(spec.Errors, e => e.Line == 0);
    }

    [Fact]
    public void Extract_reads_leading_block_and_line_comments_and_stops_at_the_first_token()
    {
        Assert.Equal("# A\nRole: Shell", SpecComment.Extract("/*\n# A\nRole: Shell\n*/\nusing X;\n/* later */"));
        Assert.Equal("# A\nRole: Shell", SpecComment.Extract("﻿// # A\n// Role: Shell\nusing X;"));
        Assert.Null(SpecComment.Extract("using X;\n/* not leading */"));
        Assert.Null(SpecComment.Extract("/* unterminated"));
        Assert.Null(SpecComment.Extract("/*   */ class A {}"));
    }
}

public sealed class SpecManifestAnalyzerFacts
{
    [Fact]
    public Task Undescribed_handlers_unhandled_operations_and_bad_headers_report_DB0024() => Verify<SpecManifestAnalyzer>.Diagnostics("""
        /*
        # A

        Role: Admin
        Shipped: true

        Invoking "open" shows it. Invoking "export" saves it.
        */
        using System.Threading.Tasks;
        using DigitalBrain;
        public sealed record Opened : Signal;
        [App("a")] public sealed class {|DB0024:{|DB0024:A|}|} : App
        {
            [On] public Task Open(Opened s) => Task.CompletedTask;
            [On] public Task {|DB0024:Close|}(Opened s) => Task.CompletedTask;
        }
        """);

    [Fact]
    public Task Spec_and_handlers_that_agree_report_nothing() => Verify<SpecManifestAnalyzer>.Diagnostics("""
        /*
        # A

        Role: Optional
        Shipped: false

        Invoking "open" shows it. The dock posts "clear-all".
        */
        using System.Threading.Tasks;
        using DigitalBrain;
        public sealed record Opened : Signal;
        [App("a")] public sealed class A : App
        {
            [On] public Task Open(Opened s) => Task.CompletedTask;
            [On] public Task ClearAll(Opened s) => Task.CompletedTask;
            public Task NotAnOperation() => Task.CompletedTask;
        }
        public sealed class NotApp { [On] public Task Other(Opened s) => Task.CompletedTask; }
        """);

    [Fact]
    public Task App_without_spec_is_left_to_DB0027() => Verify<SpecManifestAnalyzer>.Diagnostics("""
        using System.Threading.Tasks;
        using DigitalBrain;
        public sealed record Opened : Signal;
        [App("a")] public sealed class A : App { [On] public Task Open(Opened s) => Task.CompletedTask; }
        """);

    [Fact]
    public Task Sibling_spec_file_is_read_with_the_same_parser()
    {
        var test = Verify<SpecManifestAnalyzer>.Create("""
            using System.Threading.Tasks;
            using DigitalBrain;
            public sealed record Opened : Signal;
            [App("notes")] public sealed class {|DB0024:Notes|} : App { [On] public Task Open(Opened s) => Task.CompletedTask; }
            """);
        test.TestState.AdditionalFiles.Add(("Notes.spec.md", "# Notes\n\nOperations: open, archive\n"));
        return test.RunAsync(TestContext.Current.CancellationToken);
    }
}

public sealed class UntypedJsonAnalyzerFacts
{
    [Fact]
    public Task Hand_written_json_in_app_reports_DB0025() => Verify<UntypedJsonAnalyzer>.Diagnostics("""
        using System.Text.Json;
        using System.Text.Json.Nodes;
        using DigitalBrain;
        public sealed class A : App
        {
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
    public Task Typed_records_and_code_outside_apps_report_nothing() => Verify<UntypedJsonAnalyzer>.Diagnostics("""
        using System.Text.Json;
        using System.Text.Json.Nodes;
        using DigitalBrain;
        public sealed record Payload(int Count);
        public sealed class A : App
        {
            public string Typed(int n) => JsonSerializer.Serialize(new Payload(n));
            public Payload? Back(string s) => JsonSerializer.Deserialize<Payload>(s);
            public string Plain(string v) => "name: " + v;
        }
        public sealed class Helper { public string? Node(string s) => JsonNode.Parse(s)?.ToString(); }
        """);
}

public sealed class BaseBoilerplateAnalyzerFacts
{
    [Fact]
    public Task Re_implemented_key_ledger_and_present_report_DB0026() => Verify<BaseBoilerplateAnalyzer>.Diagnostics("""
        using System.Threading.Tasks;
        using DigitalBrain;
        public sealed class Brain { }
        [App("a")] public sealed class A : App
        {
            public static string {|DB0026:Key|}(Brain brain) => "a";
            public Task {|DB0026:Present|}() => Task.CompletedTask;
            private sealed class {|DB0026:RequestLedger|} { }
        }
        public static class AHelpers
        {
            public static string {|DB0026:Key|}(Brain brain) => "a";
        }
        public sealed class {|DB0026:FilesRequestLedger|} { }
        """);

    [Fact]
    public Task Look_alikes_and_compilations_without_an_app_report_nothing() => Verify<BaseBoilerplateAnalyzer>.Diagnostics("""
        using DigitalBrain;
        public sealed class Brain { }
        public static class Keys { public static string Key(Brain brain) => "a"; }
        public sealed class RequestLedger { }
        public sealed class Viewer { public void Present() { } }
        """);

    [Fact]
    public Task Key_with_other_shapes_reports_nothing() => Verify<BaseBoilerplateAnalyzer>.Diagnostics("""
        using DigitalBrain;
        public sealed class Brain { }
        [App("a")] public sealed class A : App
        {
            public string Key() => "a";
            public int Key(Brain brain, int i) => i;
        }
        """);
}

public sealed class AnalyzerSetFacts
{
    [Fact]
    public void All_holds_fourteen_analyzers_covering_every_rule_once()
    {
        Assert.Equal(14, AnalyzerSet.All.Length);
        var ids = AnalyzerSet.All.SelectMany(a => a.SupportedDiagnostics).Select(d => d.Id).ToArray();
        Assert.Equal(
            ["DB0001", "DB0002", "DB0003", "DB0013", "DB0014", "DB0020", "DB0021", "DB0022", "DB0023", "DB0024", "DB0025", "DB0026", "DB0027", "DB0028"],
            ids.Order(StringComparer.Ordinal));
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(AnalyzerSet.All, a => a is ReactionShapeAnalyzer);
    }

    [Fact]
    public void Every_app_rule_has_an_analyzer_in_the_set()
    {
        var ids = AnalyzerSet.All.SelectMany(a => a.SupportedDiagnostics).Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        Assert.All(AppRules.All, d => Assert.Contains(d.Id, ids));
        Assert.True(typeof(AppRules).IsPublic);
        Assert.True(typeof(AnalyzerSet).IsPublic);
    }
}
