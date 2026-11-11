using System.Threading.Tasks;
using DigitalBrain.Analyzers.Screens;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace DigitalBrain.Analyzers.Tests.Unit.Screens;

public sealed class EmptyCatchChallengerFacts
{
    [Fact]
    public Task Screen_WithEmptyCatch_OrReturnOrContinue_EmitsDB0022() => Verify<EmptyCatchAnalyzer>.Diagnostics("""
        using System;
        using DigitalBrain;
        using DigitalBrain.UI;

        public sealed class SState { }

        public sealed class S : Screen<SState>
        {
            protected override View Render(SState state) => new("S", 1);

            public void EmptyCatch()
            {
                try { Console.WriteLine(); }
                {|DB0022:catch|} { }
            }

            public void ReturnCatch()
            {
                try { Console.WriteLine(); }
                {|DB0022:catch|} (Exception) { return; }
            }

            public void ContinueCatch()
            {
                while (true)
                {
                    try { Console.WriteLine(); }
                    {|DB0022:catch|} (Exception) { continue; }
                }
            }
        }
        """);

    [Fact]
    public Task Screen_WithNonSwallowingCatch_DoesNotEmitDB0022() => Verify<EmptyCatchAnalyzer>.Diagnostics("""
        using System;
        using DigitalBrain;
        using DigitalBrain.UI;

        public sealed class SState { }

        public sealed class S : Screen<SState>
        {
            protected override View Render(SState state) => new("S", 1);

            public void RethrowCatch()
            {
                try { Console.WriteLine(); }
                catch (Exception) { throw; }
            }

            public void LoggingCatch()
            {
                try { Console.WriteLine(); }
                catch (Exception e) { Console.WriteLine(e.Message); }
            }

            public int ReturnValueCatch()
            {
                try { return 42; }
                catch (Exception) { return -1; }
            }
        }
        """);

    [Fact]
    public Task NestedClassInsideScreen_WithEmptyCatch_EmitsDB0022() => Verify<EmptyCatchAnalyzer>.Diagnostics("""
        using System;
        using DigitalBrain;
        using DigitalBrain.UI;

        public sealed class SState { }

        public sealed class S : Screen<SState>
        {
            protected override View Render(SState state) => new("S", 1);

            public sealed class InnerHelper
            {
                public void Swallow()
                {
                    try { Console.WriteLine(); }
                    {|DB0022:catch|} { }
                }
            }
        }
        """);

    [Fact]
    public Task NonScreenNeuron_WithEmptyCatch_DoesNotEmitDB0022() => Verify<EmptyCatchAnalyzer>.Diagnostics("""
        using System;
        using DigitalBrain;

        public sealed class WorkerNeuron : Neuron
        {
            public void Swallow()
            {
                try { Console.WriteLine(); }
                catch { }
            }
        }
        """);

    [Fact]
    public Task PlainClass_WithEmptyCatch_DoesNotEmitDB0022() => Verify<EmptyCatchAnalyzer>.Diagnostics("""
        using System;

        public sealed class OrdinaryService
        {
            public void Swallow()
            {
                try { Console.WriteLine(); }
                catch { }
            }
        }
        """);
}

public sealed class UntypedJsonChallengerFacts
{
    [Fact]
    public Task Screen_WithUntypedJsonForms_EmitsDB0025() => Verify<UntypedJsonAnalyzer>.Diagnostics("""
        using System.Text.Json;
        using System.Text.Json.Nodes;
        using DigitalBrain;
        using DigitalBrain.UI;

        public sealed class SState { }

        public sealed class S : Screen<SState>
        {
            protected override View Render(SState state) => new("S", 1);

            public string Anon(int n) => {|DB0025:JsonSerializer.Serialize(new { Value = n })|};
            public string Boxed(object obj) => {|DB0025:JsonSerializer.Serialize(obj)|};
            public string? ParseNode(string s) => {|DB0025:JsonNode.Parse(s)|}?["k"]?.ToString();
            public JsonObject CreateObj() => {|DB0025:new JsonObject()|};
            public JsonArray CreateArr() => {|DB0025:new JsonArray()|};
            public int ParseDoc(string s) { using var doc = {|DB0025:JsonDocument.Parse(s)|}; return 1; }
            public string Concat(string val) => {|DB0025:"{\"name\":\"" + val + "\"}"|};
            public string Interpolated(string val) => {|DB0025:$"{{\"count\":{val}}}"|};
        }
        """);

    [Fact]
    public Task Screen_WithTypedRecordAndNormalStrings_DoesNotEmitDB0025() => Verify<UntypedJsonAnalyzer>.Diagnostics("""
        using System.Text.Json;
        using DigitalBrain;
        using DigitalBrain.UI;

        public sealed record TypedPayload(int Value);
        public sealed class SState { }

        public sealed class S : Screen<SState>
        {
            protected override View Render(SState state) => new("S", 1);

            public string Typed(int n) => JsonSerializer.Serialize(new TypedPayload(n));
            public TypedPayload? Back(string s) => JsonSerializer.Deserialize<TypedPayload>(s);
            public string NormalConcat(string s) => "Result: " + s;
            public string NormalInterpolated(string s) => $"Result is {s}";
            public string ColonsWithoutQuotes(string s) => "key: " + s;
        }
        """);

    [Fact]
    public Task NonScreenNeuron_WithUntypedJson_DoesNotEmitDB0025() => Verify<UntypedJsonAnalyzer>.Diagnostics("""
        using System.Text.Json;
        using System.Text.Json.Nodes;
        using DigitalBrain;

        public sealed class PlainNeuron : Neuron
        {
            public string Anon(int n) => JsonSerializer.Serialize(new { Value = n });
            public JsonObject CreateObj() => new JsonObject();
            public string? Parse(string s) => JsonNode.Parse(s)?.ToString();
        }
        """);

    [Fact]
    public Task PlainClass_WithUntypedJson_DoesNotEmitDB0025() => Verify<UntypedJsonAnalyzer>.Diagnostics("""
        using System.Text.Json;
        using System.Text.Json.Nodes;

        public sealed class OrdinaryService
        {
            public string Anon(int n) => JsonSerializer.Serialize(new { Value = n });
            public JsonObject CreateObj() => new JsonObject();
            public string? Parse(string s) => JsonNode.Parse(s)?.ToString();
        }
        """);
}

public sealed class SpecCommentChallengerFacts
{
    [Fact]
    public Task ConcreteScreen_WithoutSpecComment_EmitsDB0027() => Verify<SpecCommentAnalyzer>.Diagnostics("""
        using DigitalBrain;
        using DigitalBrain.UI;

        public sealed class SState { }

        public sealed class {|DB0027:MissingSpecScreen|} : Screen<SState>
        {
            protected override View Render(SState state) => new("Screen", 1);
        }
        """);

    [Fact]
    public Task ConcreteScreen_WithSpecComment_DoesNotEmitDB0027() => Verify<SpecCommentAnalyzer>.Diagnostics("""
        /*
        # Spec Screen
        Role: Example
        Shipped: true
        A screen with a valid spec comment.
        */
        using DigitalBrain;
        using DigitalBrain.UI;

        public sealed class SState { }

        public sealed class ValidSpecScreen : Screen<SState>
        {
            protected override View Render(SState state) => new("Screen", 1);
        }
        """);

    [Fact]
    public Task AbstractScreen_WithoutSpecComment_DoesNotEmitDB0027() => Verify<SpecCommentAnalyzer>.Diagnostics("""
        using DigitalBrain;
        using DigitalBrain.UI;

        public abstract class AbstractBaseScreen<TState> : Screen<TState> where TState : class, new()
        {
            protected override View Render(TState state) => new("Abstract", 1);
        }
        """);

    [Fact]
    public Task NonScreenNeuron_WithoutSpecComment_DoesNotEmitDB0027() => Verify<SpecCommentAnalyzer>.Diagnostics("""
        using DigitalBrain;

        public sealed class RegularNeuron : Neuron
        {
            public void Work() { }
        }
        """);

    [Fact]
    public Task PlainClass_WithoutSpecComment_DoesNotEmitDB0027() => Verify<SpecCommentAnalyzer>.Diagnostics("""
        public sealed class RegularClass
        {
            public void Work() { }
        }
        """);
}

public sealed class SuppressionChallengerFacts
{
    private static Task Run(string source)
    {
        var test = Verify<SuppressionAnalyzer>.Create(source);
        test.TestBehaviors |= TestBehaviors.SkipSuppressionCheck;
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task ScreenFile_SuppressingDBRules_EmitsDB0028() => Run("""
        using System.Diagnostics.CodeAnalysis;
        using DigitalBrain;
        using DigitalBrain.UI;

        public sealed class SState { }

        public sealed class S : Screen<SState>
        {
            protected override View Render(SState state) => new("S", 1);

            {|DB0028:#pragma warning disable DB0022|}
            public void M1() { }
            {|DB0028:#pragma warning restore DB0022|}

            {|DB0028:#pragma warning disable DB0025|}
            public void M2() { }
            {|DB0028:#pragma warning restore DB0025|}

            [{|DB0028:SuppressMessage("DigitalBrain.Screens", "DB0022")|}]
            public void M3() { }

            [{|DB0028:UnconditionalSuppressMessage("DigitalBrain.Screens", "DB0027")|}]
            public void M4() { }

            [{|DB0028:SuppressMessage("AnyCategory", "DB0025:WithColonJustification")|}]
            public void M5() { }
        }
        """);

    [Fact]
    public Task ScreenFile_SuppressingNonDBRules_DoesNotEmitDB0028() => Run("""
        using System.Diagnostics.CodeAnalysis;
        using DigitalBrain;
        using DigitalBrain.UI;

        public sealed class SState { }

        public sealed class S : Screen<SState>
        {
            protected override View Render(SState state) => new("S", 1);

            #pragma warning disable CS0168
            public void M1() { int unused; }
            #pragma warning restore CS0168

            [SuppressMessage("Compiler", "CS0219")]
            public void M2() { int unused = 1; }
        }
        """);

    [Fact]
    public Task NonScreenNeuronFile_SuppressingDBRules_DoesNotEmitDB0028() => Run("""
        using System.Diagnostics.CodeAnalysis;
        using DigitalBrain;

        public sealed class PlainNeuron : Neuron
        {
            #pragma warning disable DB0022
            public void M1() { }
            #pragma warning restore DB0022

            [SuppressMessage("DigitalBrain.Screens", "DB0022")]
            public void M2() { }
        }
        """);

    [Fact]
    public Task PlainClassFile_SuppressingDBRules_DoesNotEmitDB0028() => Run("""
        using System.Diagnostics.CodeAnalysis;

        public sealed class PlainClass
        {
            #pragma warning disable DB0022
            public void M1() { }
            #pragma warning restore DB0022

            [SuppressMessage("DigitalBrain.Screens", "DB0022")]
            public void M2() { }
        }
        """);
}
