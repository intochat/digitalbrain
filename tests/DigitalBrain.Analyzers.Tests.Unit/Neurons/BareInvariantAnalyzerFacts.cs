using System.Collections.Immutable;
using DigitalBrain.Analyzers.CodeFixes;
using DigitalBrain.Analyzers.Neurons;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DigitalBrain.Analyzers.Tests.Unit.Neurons;

public sealed class BareInvariantAnalyzerFacts
{
    // DB0040 is off by default; every fact turns it on the way a consumer does.
    private const string Enable = "root = true\n[*]\ndotnet_diagnostic.DB0040.severity = warning\n";

    private static Task Diagnostics(string source)
    {
        var test = Verify<BareInvariantAnalyzer>.Create(source);
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", Enable));
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task ReportsBareInvalidOperationInPublicNeuronMethod() => Diagnostics("""
        using System;
        using System.Threading.Tasks;
        using DigitalBrain;
        public abstract class Base : Neuron { }
        public sealed class Cart : Base
        {
            public Task Add(int quantity)
            {
                if (quantity > 10) {|DB0040:throw new InvalidOperationException($"Cart holds at most ten, not {quantity}");|}
                return Task.CompletedTask;
            }
            public int Take(string name) => name.Length > 3 ? name.Length : {|DB0040:throw new ArgumentException("Name too short", nameof(name))|};
            public void Read() { {|DB0040:throw new System.IO.InvalidDataException("Corrupt cart");|} }
        }
        """);

    [Fact]
    public Task ReportsInsideOnReaction() => Diagnostics("""
        using System;
        using DigitalBrain;
        public sealed record Placed : Signal;
        public sealed class Orders : App
        {
            [On] private void When(Placed placed) { {|DB0040:throw new InvalidOperationException("Order already placed");|} }
        }
        """);

    [Fact]
    public Task GuardClausesAndUnauthorizedAreExempt() => Diagnostics("""
        using System;
        using DigitalBrain;
        public sealed class Cart : Neuron
        {
            public void Add(string name, object item)
            {
                ArgumentNullException.ThrowIfNull(item);
                ArgumentException.ThrowIfNullOrWhiteSpace(name);
                if (name == "root") throw new UnauthorizedAccessException("Only the owner may add");
                if (name == "x") throw new ArgumentException(nameof(name));
                try { Console.WriteLine(name); }
                catch (FormatException error) { throw new InvalidOperationException("Could not print", error); }
            }
        }
        public sealed class Plain
        {
            public void Add() => throw new InvalidOperationException("Not a neuron");
        }
        """);

    [Fact]
    public Task PrivateMethodsAreExempt() => Diagnostics("""
        using System;
        using DigitalBrain;
        public sealed class Cart : Neuron
        {
            private void A() => throw new InvalidOperationException("private");
            protected void B() => throw new InvalidOperationException("protected");
            internal void C() => throw new InvalidOperationException("internal");
        }
        """);

    [Fact]
    public void DisabledByDefault() => Assert.False(Rules.BareInvariant.IsEnabledByDefault);

    [Fact]
    public async Task CodeFixExtractsSingleThrowIfToRule()
    {
        var (main, rule) = await ApplyFix("""
            using System;
            using System.Threading.Tasks;
            using DigitalBrain;

            namespace Shop;

            public sealed class Cart : Neuron
            {
                private int Limit { get; } = 10;

                public async Task Add(int quantity)
                {
                    var reserved = 2;
                    if (quantity + reserved > this.Limit)
                    {
                        throw new InvalidOperationException("Cart cannot hold more than ten items");
                    }
                    await Task.Yield();
                }
            }
            """);
        Assert.Equal("""
            using System;
            using System.Threading.Tasks;
            using DigitalBrain;

            namespace Shop;

            public sealed class Cart : Neuron
            {
                private int Limit { get; } = 10;

                public async Task Add(int quantity)
                {
                    var reserved = 2;
                    await Check("add", new CartCannotHoldMoreThan(quantity, reserved, this.Limit));
                    await Task.Yield();
                }
            }
            """, main.ReplaceLineEndings("\n"));
        Assert.Equal("""
            using DigitalBrain;
            using DigitalBrain.Rules;
            using System;
            using System.Threading.Tasks;

            namespace Shop;

            internal sealed record CartCannotHoldMoreThan(int quantity, int reserved, int Limit) : Rule("cart-cannot-hold-more-than")
            {
                public override bool Broken => quantity + reserved > Limit;
                public override string Message => "Cart cannot hold more than ten items";
            }

            """, rule.ReplaceLineEndings("\n"));
    }

    [Fact]
    public Task RuleViolationExceptionIsNotReported() => Diagnostics("""
        using DigitalBrain;
        namespace DigitalBrain.Rules { public sealed class RuleViolationException(string message) : System.Exception(message); }
        public sealed class Cart : Neuron
        {
            public void Add() => throw new DigitalBrain.Rules.RuleViolationException("Cart is full");
        }
        """);

    [Fact]
    public async Task FixRenamesCapturesThatCollideWithRuleMembers()
    {
        var (main, rule) = await ApplyFix("""
            using System;
            using System.Threading.Tasks;
            using DigitalBrain;

            namespace Shop;

            public sealed class Cart : Neuron
            {
                public string Name { get; } = "";

                public async Task Add(string Message)
                {
                    if (Name == Message) throw new InvalidOperationException("Cart cannot hold more than itself");
                    await Task.Yield();
                }
            }
            """);
        Assert.Contains("await Check(\"add\", new CartCannotHoldMoreThan(Name, Message));", main, StringComparison.Ordinal);
        Assert.Contains("record CartCannotHoldMoreThan(string NameValue, string MessageValue)", rule, StringComparison.Ordinal);
        Assert.Contains("Broken => NameValue == MessageValue;", rule, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FixCapturesAPrivateConstAndDeclinesANestedType()
    {
        var (main, rule) = await ApplyFix("""
            using System;
            using System.Threading.Tasks;
            using DigitalBrain;

            namespace Shop;

            public sealed class Cart : Neuron
            {
                private const int Max = 10;

                public async Task Add(int quantity)
                {
                    if (quantity > Max) throw new InvalidOperationException("Cart cannot hold more than ten");
                    await Task.Yield();
                }
            }
            """);
        Assert.Contains("new CartCannotHoldMoreThan(quantity, Max)", main, StringComparison.Ordinal);
        Assert.Contains("Broken => quantity > Max;", rule, StringComparison.Ordinal);
        Assert.Empty(await Offered("""
            using System;
            using System.Threading.Tasks;
            using DigitalBrain;
            public sealed class Cart : Neuron
            {
                private enum Mode { Open, Closed }
                public async Task Add(object mode) { if (mode is Mode) throw new InvalidOperationException("Wrong mode"); await Task.Yield(); }
            }
            """));
    }

    [Fact]
    public async Task NoFixWhenTheMethodIsNotAsyncOrTheConditionCalls()
    {
        Assert.Empty(await Offered("""
            using System;
            using DigitalBrain;
            public sealed class Cart : Neuron
            {
                public void Add(int quantity) { if (quantity > 1) throw new InvalidOperationException("Too many"); }
            }
            """));
        Assert.Empty(await Offered("""
            using System;
            using System.Threading.Tasks;
            using DigitalBrain;
            public sealed class Cart : Neuron
            {
                public async Task Add(string name) { if (name.Contains('x')) throw new InvalidOperationException("No x"); await Task.Yield(); }
            }
            """));
    }

    private static async Task<(Document Document, Diagnostic Diagnostic)> Prepare(string source, CancellationToken ct)
    {
        var references = await Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, ct);
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "Fix", "Fix", LanguageNames.CSharp,
            filePath: "/src/Fix.csproj",
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                specificDiagnosticOptions: ImmutableDictionary<string, ReportDiagnostic>.Empty.Add("DB0040", ReportDiagnostic.Warn)),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: references));
        project = project.AddDocument("Stubs.cs", SourceText.From(Verify<BareInvariantAnalyzer>.Stubs), filePath: "/src/Stubs.cs").Project;
        var document = project.AddDocument("Cart.cs", SourceText.From(source), filePath: "/src/Cart.cs");
        var compilation = (await document.Project.GetCompilationAsync(ct))!;
        var diagnostics = await compilation.WithAnalyzers([new BareInvariantAnalyzer()]).GetAnalyzerDiagnosticsAsync(ct);
        return (document, Assert.Single(diagnostics));
    }

    private static async Task<List<CodeAction>> Offered(string source)
    {
        var ct = TestContext.Current.CancellationToken;
        var (document, diagnostic) = await Prepare(source, ct);
        var actions = new List<CodeAction>();
        await new ExtractToRuleFix().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), ct));
        return actions;
    }

    private static async Task<(string Main, string Rule)> ApplyFix(string source)
    {
        var ct = TestContext.Current.CancellationToken;
        var (document, diagnostic) = await Prepare(source, ct);
        var actions = new List<CodeAction>();
        await new ExtractToRuleFix().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), ct));
        var action = Assert.Single(actions);
        var operations = await action.GetOperationsAsync(ct);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var project = solution.GetProject(document.Project.Id)!;
        var added = Assert.Single(project.Documents, d => d.Name == "CartCannotHoldMoreThan.cs");
        Assert.Equal(["Rules"], added.Folders);
        // Consumers build with warnings as errors: the fix may add no warning or error the code did not already have.
        static string Key(Diagnostic d) => d.Id + ": " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        var before = (await document.Project.GetCompilationAsync(ct))!.GetDiagnostics(ct)
            .Where(d => d.Severity >= DiagnosticSeverity.Warning).Select(Key).ToHashSet();
        var introduced = (await project.GetCompilationAsync(ct))!.GetDiagnostics(ct)
            .Where(d => d.Severity >= DiagnosticSeverity.Warning).Select(Key).Where(k => !before.Contains(k));
        Assert.Empty(introduced);
        return ((await project.GetDocument(document.Id)!.GetTextAsync(ct)).ToString(), (await added.GetTextAsync(ct)).ToString());
    }
}
