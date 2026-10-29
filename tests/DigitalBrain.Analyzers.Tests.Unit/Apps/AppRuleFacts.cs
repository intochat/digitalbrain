using DigitalBrain.Analyzers.Apps;
using Microsoft.CodeAnalysis;

namespace DigitalBrain.Analyzers.Tests.Unit.Apps;

public sealed class AppRulesFacts
{
    [Fact]
    public void Nine_app_ids_are_reserved_in_order_with_handbook_links()
    {
        Assert.Equal(["DB0020", "DB0021", "DB0022", "DB0023", "DB0024", "DB0025", "DB0026", "DB0027", "DB0028"], AppRules.All.Select(d => d.Id));
        Assert.All(AppRules.All, d =>
        {
            Assert.Equal("DigitalBrain.Apps", d.Category);
            Assert.True(d.IsEnabledByDefault);
            Assert.Equal("https://github.com/intochat/handbook/blob/main/reference/app-convention.md#" + d.Id.ToLowerInvariant(), d.HelpLinkUri);
        });
        Assert.Same(AppRules.Suppression, AppRules.ById("DB0028"));
        Assert.Null(AppRules.ById("DB0009"));
    }

    [Fact]
    public void Severities_follow_the_convention_table()
    {
        foreach (var id in new[] { "DB0020", "DB0021", "DB0022", "DB0023", "DB0027", "DB0028" })
        {
            Assert.Equal(DiagnosticSeverity.Error, AppRules.ById(id)!.DefaultSeverity);
        }
        foreach (var id in new[] { "DB0024", "DB0025", "DB0026" })
        {
            Assert.Equal(DiagnosticSeverity.Warning, AppRules.ById(id)!.DefaultSeverity);
        }
    }

    [Fact]
    public void Every_app_id_is_a_release_tracking_row()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DigitalBrain.Analyzers", "AnalyzerReleases.Unshipped.md");
        var text = File.ReadAllText(path);
        Assert.All(AppRules.All, d => Assert.Contains(d.Id + " | DigitalBrain.Apps | " + d.DefaultSeverity, text, StringComparison.Ordinal));
    }
}

public sealed class AppBaseAnalyzerFacts
{
    [Fact]
    public Task App_attribute_without_App_base_reports_DB0020() => Verify<AppBaseAnalyzer>.Diagnostics("""
        using DigitalBrain;
        [App("a")] public sealed class {|DB0020:A|} { }
        public abstract class NotApp { }
        [App("b")] public sealed class {|DB0020:B|} : NotApp { }
        """);

    [Fact]
    public Task App_attribute_with_direct_or_indirect_App_base_reports_nothing() => Verify<AppBaseAnalyzer>.Diagnostics("""
        using DigitalBrain;
        [App("a")] public sealed class A : App { }
        public abstract class Base : App { }
        [App("b")] public sealed class B : Base { }
        public sealed class Plain { }
        """);
}

public sealed class VendorRequiresAnalyzerFacts
{
    [Fact]
    public Task Module_ids_in_app_capability_ids_report_DB0021() => Verify<VendorRequiresAnalyzer>.Diagnostics("""
        using DigitalBrain;
        using DigitalBrain.Capabilities;
        [Module("acme.files")] public sealed class AcmeModule : Module { }
        [App("a")] public sealed class A : App
        {
            public CapabilityId[] Requires() =>
            [
                CapabilityId.Parse({|DB0021:"google.drive@1"|}),
                CapabilityId.Parse({|DB0021:"microsoft.onedrive@1"|}),
                new CapabilityId({|DB0021:"postgres"|}, 1),
                CapabilityId.Parse({|DB0021:"acme.files@1"|}),
            ];
        }
        """);

    [Fact]
    public Task Capability_names_or_non_app_code_report_nothing() => Verify<VendorRequiresAnalyzer>.Diagnostics("""
        using DigitalBrain;
        using DigitalBrain.Capabilities;
        [App("a")] public sealed class A : App
        {
            public CapabilityId[] Requires() => [CapabilityId.Parse("storage@1"), new CapabilityId("tables", 1)];
        }
        public sealed class GoogleDriveModule
        {
            public CapabilityId Provides() => CapabilityId.Parse("google.drive@1");
        }
        """);
}

public sealed class EmptyCatchAnalyzerFacts
{
    [Fact]
    public Task Empty_and_swallow_only_catches_in_app_report_DB0022() => Verify<EmptyCatchAnalyzer>.Diagnostics("""
        using System;
        using DigitalBrain;
        public sealed class A : App
        {
            public void Bare() { try { } {|DB0022:catch|} { } }
            public void Typed() { try { } {|DB0022:catch|} (Exception) { } }
            public void Returns() { try { } {|DB0022:catch|} (Exception) { return; } }
            public void Continues() { foreach (var i in new int[1]) { try { } {|DB0022:catch|} { continue; } } }
        }
        """);

    [Fact]
    public Task Catches_that_record_state_rethrow_or_live_outside_apps_report_nothing() => Verify<EmptyCatchAnalyzer>.Diagnostics("""
        using System;
        using System.IO;
        using DigitalBrain;
        public sealed class A : App
        {
            private string state = "";
            public void Records(bool retry) { try { } catch (IOException e) when (retry) { state = e.GetType().Name; } }
            public void Rethrows() { try { } catch (Exception) { throw; } }
            public int Fallback() { try { return 1; } catch (FormatException) { return 0; } }
        }
        public sealed class Helper { public void Swallow() { try { } catch { } } }
        """);
}

public sealed class KeyCompositionAnalyzerFacts
{
    [Fact]
    public Task String_composed_keys_in_app_report_DB0023() => Verify<KeyCompositionAnalyzer>.Diagnostics("""
        using DigitalBrain;
        public sealed class A : App
        {
            private readonly string appKey = "a";
            public string Plus() => {|DB0023:appKey + "/items"|};
            public string Chain(string id) => {|DB0023:appKey + "/items/" + id|};
            public string Interpolated(string id) => {|DB0023:$"{appKey}/items/{id}"|};
            public string Concat() => {|DB0023:string.Concat(appKey, "/items")|};
        }
        """);

    [Fact]
    public Task Non_key_strings_and_code_outside_apps_report_nothing() => Verify<KeyCompositionAnalyzer>.Diagnostics("""
        using DigitalBrain;
        public sealed class A : App
        {
            private readonly string appKey = "a";
            public string Path(string folder) => folder + "/file";
            public string Label() => appKey + " (draft)";
        }
        public sealed class Helper
        {
            public string Build(string appKey) => appKey + "/items";
        }
        """);
}

public sealed class SpecCommentAnalyzerFacts
{
    [Fact]
    public Task App_without_spec_comment_reports_DB0027() => Verify<SpecCommentAnalyzer>.Diagnostics("""
        using DigitalBrain;
        // a line comment is not a spec comment
        [App("a")] public sealed class {|DB0027:A|} : App { }
        """);

    [Fact]
    public Task App_with_leading_spec_comment_reports_nothing() => Verify<SpecCommentAnalyzer>.Diagnostics("""
        /*
        # Calculator

        Role: Example
        Shipped: true
        */
        using DigitalBrain;
        [App("calculator")] public sealed class Calculator : App { }
        public abstract class Base : App { }
        """);

    [Fact]
    public Task App_with_sibling_spec_file_reports_nothing()
    {
        var test = Verify<SpecCommentAnalyzer>.Create("""
            using DigitalBrain;
            [App("notes")] public sealed class Notes : App { }
            """);
        test.TestState.AdditionalFiles.Add(("Notes.spec.md", "# Notes\n\nRole: Optional\nShipped: false\n"));
        return test.RunAsync(TestContext.Current.CancellationToken);
    }
}

public sealed class SuppressionAnalyzerFacts
{
    // The verifier's own pragma-exclusion pass is skipped: DB0028 deliberately reports the pragma that tries to hide it.
    private static Task Run(string source)
    {
        var test = Verify<SuppressionAnalyzer>.Create(source);
        test.TestBehaviors |= Microsoft.CodeAnalysis.Testing.TestBehaviors.SkipSuppressionCheck;
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task Pragmas_naming_DB_ids_in_app_files_report_DB0028() => Run("""
        using DigitalBrain;
        {|DB0028:#pragma warning disable DB0022|}
        {|DB0028:#pragma warning restore DB0022|}
        #pragma warning disable CS0168
        #pragma warning restore CS0168
        [App("a")] public sealed class A : App { }
        """);

    // A pragma aimed at DB0028 is itself reported, so any suppression attempt still fails the build.
    [Fact]
    public Task Pragma_disabling_DB0028_is_itself_reported() => Run("""
        {|DB0028:#pragma warning disable DB0028|}
        using DigitalBrain;
        [App("a")] public sealed class A : App { }
        """);

    [Fact]
    public Task SuppressMessage_with_DB_id_or_category_in_app_files_reports_DB0028() => Run("""
        using System.Diagnostics.CodeAnalysis;
        using DigitalBrain;
        [App("a")] public sealed class A : App
        {
            [{|DB0028:SuppressMessage("DigitalBrain.Apps", "DB0022")|}] public void M1() { }
            [{|DB0028:SuppressMessage("Other", "DB0023:Keys")|}] public void M2() { }
            [{|DB0028:UnconditionalSuppressMessage("DigitalBrain", "Anything")|}] public void M3() { }
            [SuppressMessage("Style", "IDE0060")] public void M4() { }
        }
        """);

    [Fact]
    public Task Suppressions_outside_app_files_report_nothing() => Run("""
        using System.Diagnostics.CodeAnalysis;
        #pragma warning disable DB0022
        public sealed class Helper { [SuppressMessage("DigitalBrain.Apps", "DB0022")] public void M() { } }
        #pragma warning restore DB0022
        """);
}
