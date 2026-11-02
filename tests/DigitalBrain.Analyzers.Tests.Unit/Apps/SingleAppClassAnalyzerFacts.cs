using System.Threading.Tasks;
using DigitalBrain.Analyzers.Apps;
using Xunit;

namespace DigitalBrain.Analyzers.Tests.Unit.Apps;

public sealed class SingleAppClassAnalyzerFacts
{
    [Fact]
    public Task Single_app_class_reports_nothing() => Verify<SingleAppClassAnalyzer>.Diagnostics("""
        using DigitalBrain;
        [App("app1")]
        public sealed class App1 : App { }
        """);

    [Fact]
    public Task Multiple_app_classes_report_DB0030() => Verify<SingleAppClassAnalyzer>.Diagnostics("""
        using DigitalBrain;
        [App("app1")]
        public sealed class {|DB0030:App1|} : App { }

        [App("app2")]
        public sealed class {|DB0030:App2|} : App { }
        """);

    [Fact]
    public Task Abstract_app_class_reports_DB0030() => Verify<SingleAppClassAnalyzer>.Diagnostics("""
        using DigitalBrain;
        [App("app1")]
        public abstract class {|DB0030:App1|} : App { }
        """);

    [Fact]
    public Task Generic_app_class_reports_DB0030() => Verify<SingleAppClassAnalyzer>.Diagnostics("""
        using DigitalBrain;
        [App("generic-app")]
        public sealed class {|DB0030:GenericApp|}<T> : App { }
        """);

    [Fact]
    public Task Multiple_app_classes_in_test_project_are_excluded()
    {
        var test = Verify<SingleAppClassAnalyzer>.Create("""
            using DigitalBrain;
            [App("app1")]
            public sealed class App1 : App { }

            [App("app2")]
            public sealed class App2 : App { }
            """);
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", "is_global = true\nbuild_property.IsTestProject = true\n"));
        return test.RunAsync(TestContext.Current.CancellationToken);
    }
}
