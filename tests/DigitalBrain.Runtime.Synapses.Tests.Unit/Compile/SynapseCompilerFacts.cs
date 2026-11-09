using System.Collections.Immutable;
using DigitalBrain.Runtime.Synapses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Runtime.Synapses.Tests.Unit.Compile;

public sealed class SynapseCompilerFacts
{
    // Plan 1's weekly-cap file (tests/DigitalBrain.Framework.Tests.E2E/WeeklyCap), as a synapse author writes it.
    internal const string Budget = """
        namespace Me.Budget;

        [Signal("payments.charging")]
        public sealed record Charging(decimal Amount) : Signal;

        [Signal("weeklycap.week-rolled")]
        public sealed record WeekRolled(DateOnly Week) : Signal;

        [State("week")]
        public sealed record WeekSpend(DateOnly Week = default, decimal Spent = 0)
        {
            public WeekSpend() : this(default, 0) { }
        }

        public static class Budget
        {
            [Synapse(typeof(Charging), "week")]
            public static Check WeeklyCap(Charging s, WeekSpend t) =>
                t.Spent + s.Amount > 300m
                    ? Check.Reject("weekly cap of $300")
                    : Check.Allow(t with { Spent = t.Spent + s.Amount });

            [Synapse(typeof(WeekRolled), "week")]
            public static WeekSpend NewWeek(WeekRolled s, WeekSpend t) => new(s.Week);

            [Synapse("week")]
            public static View Show(WeekSpend t) =>
                new("This week", new Column([new StatCardNode("spent", "Spent", $"{t.Spent} of 300")]));
        }
        """;

    private static SynapseSource Source(string text, string key = "Me.Budget") =>
        new(key, new Dictionary<string, string> { ["Budget.cs"] = text }, null);

    [Fact]
    public void TheWeeklyCapSourceCompiles()
    {
        var build = new SynapseCompiler(ReferenceSet.FromDefaultContext()).Compile(Source(Budget));

        Assert.True(build.Success, string.Join("\n", build.Diagnostics));
        Assert.NotEmpty(build.Assembly);
        Assert.NotEmpty(build.Symbols);
        Assert.StartsWith("synapse.Me.Budget.", build.AssemblyName, StringComparison.Ordinal);
        Assert.Equal(64, build.Hash.Length);
        Assert.EndsWith(build.Hash[..8], build.AssemblyName, StringComparison.Ordinal);
    }

    [Fact]
    public void ASyntaxErrorIsReportedWithFileAndLine()
    {
        var build = new SynapseCompiler(ReferenceSet.FromDefaultContext()).Compile(Source("namespace Me.Budget;\n\npublic static class Broken\n{\n    public static int X() => ;\n}\n"));

        Assert.False(build.Success);
        Assert.Empty(build.Assembly);
        Assert.Empty(build.Symbols);
        var error = Assert.Single(build.Diagnostics, d => d.Id == "CS1525");
        Assert.Equal("Budget.cs", error.File);
        Assert.Equal(5, error.Line);
    }

    [Fact]
    public void AShapeViolationIsReportedAsDB0041()
    {
        const string text = """
            namespace Me.Budget;

            [Signal("payments.charging")]
            public sealed record Charging(decimal Amount) : Signal;

            public static class Gates
            {
                [Synapse(typeof(Charging), "week")]
                public static Check Cap(Charging s, string t) => Check.Reject("no");
            }
            """;

        var build = new SynapseCompiler(ReferenceSet.FromDefaultContext()).Compile(Source(text));

        Assert.False(build.Success);
        var shape = Assert.Single(build.Diagnostics, d => d.Id == "DB0041");
        Assert.Equal("Budget.cs", shape.File);
        Assert.True(shape.Line > 0);
    }

    [Fact]
    public void AStateDeclaredInAnotherAssemblyIsRefusedAsDB0042()
    {
        // WeekSpend of the weekly-cap sample is compiled in its own assembly and referenced here.
        var library = new SynapseCompiler(ReferenceSet.FromDefaultContext()).Compile(Source(Budget, "Me.Library"));
        Assert.True(library.Success, string.Join("\n", library.Diagnostics));
        var references = ReferenceSet.FromDefaultContext();
        var path = Path.Combine(Path.GetTempPath(), library.AssemblyName + ".dll");
        File.WriteAllBytes(path, library.Assembly);
        var withLibrary = new ReferenceSet([.. references.References, MetadataReference.CreateFromFile(path)]);
        const string text = """
            namespace Me.Other;

            using Me.Budget;

            public static class Gates
            {
                [Synapse(typeof(Charging), "week")]
                public static Check Cap(Charging s, WeekSpend t) => Check.Reject("no");
            }
            """;

        var build = new SynapseCompiler(withLibrary).Compile(Source(text, "Me.Other"));

        Assert.False(build.Success);
        Assert.Contains(build.Diagnostics, d => d.Id == "DB0042");
    }

    [Fact]
    public void AStatusLikeStateMemberCompilesWithADB0043Warning()
    {
        const string text = """
            namespace Me.Budget;

            [Signal("payments.charging")]
            public sealed record Charging(decimal Amount) : Signal;

            [State("week")]
            public sealed record WeekSpend(decimal Spent = 0, string Status = "")
            {
                public WeekSpend() : this(0, "") { }
            }

            public static class Reactions
            {
                [Synapse(typeof(Charging), "week")]
                public static WeekSpend Charge(Charging s, WeekSpend t) => t with { Spent = t.Spent + s.Amount };
            }
            """;

        var build = new SynapseCompiler(ReferenceSet.FromDefaultContext()).Compile(Source(text));

        Assert.True(build.Success, string.Join("\n", build.Diagnostics));
        var warning = Assert.Single(build.Diagnostics, d => d.Id == "DB0043");
        Assert.Equal("Budget.cs", warning.File);
    }

    [Fact]
    public void AnOnReactionBesideAStateRecordIsRefusedAsDB0044()
    {
        const string text = """
            namespace Me.Budget;

            [State("week")]
            public sealed record WeekSpend(decimal Spent = 0)
            {
                public WeekSpend() : this(0) { }
            }

            public static class Reactions
            {
                [On]
                public static void Roll() { }
            }
            """;

        var build = new SynapseCompiler(ReferenceSet.FromDefaultContext()).Compile(Source(text));

        Assert.False(build.Success);
        Assert.Contains(build.Diagnostics, d => d.Id == "DB0044");
    }

    [Fact]
    public void AnAppClassIsRefused()
    {
        const string text = """
            namespace Me.Budget;

            public sealed class Sneaky : App
            {
            }
            """;

        var build = new SynapseCompiler(ReferenceSet.FromDefaultContext()).Compile(Source(text));

        Assert.False(build.Success);
        Assert.Contains(build.Diagnostics, d => d.Id.StartsWith("DB00", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSameSourceHashesTheSameAndIsCached()
    {
        var compiler = new SynapseCompiler(ReferenceSet.FromDefaultContext());

        var first = compiler.Compile(Source(Budget));
        var second = compiler.Compile(Source(Budget));

        Assert.True(first.Success);
        Assert.Equal(first.Hash, second.Hash);
        Assert.Same(first, second);
        Assert.Equal(1, compiler.Emits);
    }

    [Fact]
    public void AChangedReferenceSetChangesTheHash()
    {
        var full = ReferenceSet.FromDefaultContext();
        var smaller = new ReferenceSet(full.References.Skip(1).ToArray());

        Assert.NotEqual(full.Fingerprint, smaller.Fingerprint);
        var a = new SynapseCompiler(full).Compile(Source(Budget));
        var b = new SynapseCompiler(smaller).Compile(Source(Budget));
        Assert.NotEqual(a.Hash, b.Hash);
    }
    [Fact]
    public void TheSourceKeyIsPartOfTheHash()
    {
        var compiler = new SynapseCompiler(ReferenceSet.FromDefaultContext());

        var a = compiler.Compile(Source(Budget, "A"));
        var b = compiler.Compile(Source(Budget, "B"));

        Assert.NotEqual(a.Hash, b.Hash);
        Assert.StartsWith("synapse.A.", a.AssemblyName, StringComparison.Ordinal);
        Assert.StartsWith("synapse.B.", b.AssemblyName, StringComparison.Ordinal);
    }

    [Fact]
    public void ACrashingAnalyzerRefusesTheBuild()
    {
        var compiler = new SynapseCompiler(ReferenceSet.FromDefaultContext(), [new ThrowingAnalyzer()]);

        var build = compiler.Compile(Source(Budget));

        Assert.False(build.Success);
        Assert.Empty(build.Assembly);
        Assert.Contains(build.Diagnostics, d => d.Id == "AD0001");
    }

    [Fact]
    public void TwoCompilersEmitTheSameBytes()
    {
        var first = new SynapseCompiler(ReferenceSet.FromDefaultContext()).Compile(Source(Budget));
        var second = new SynapseCompiler(ReferenceSet.FromDefaultContext()).Compile(Source(Budget));

        Assert.True(first.Success);
        Assert.NotSame(first, second);
        Assert.Equal(first.Assembly, second.Assembly);
        Assert.Equal(first.Symbols, second.Symbols);
    }

    [Fact]
    public void OversizedCompiledAssemblyIsRefusedAsDB0048()
    {
        var largeLiteral = new string('A', (SynapseCompiler.MaxAssemblyBytes / 2) + 4096);
        var sourceText = $$"""
            namespace Me.Budget;

            [State("week")]
            public sealed record WeekSpend(decimal Spent = 0)
            {
                public WeekSpend() : this(0) { }
                public static string Payload => "{{largeLiteral}}";
            }
            """;

        var build = new SynapseCompiler(ReferenceSet.FromDefaultContext()).Compile(Source(sourceText, "Me.Oversized"));

        Assert.False(build.Success);
        Assert.Empty(build.Assembly);
        var error = Assert.Single(build.Diagnostics, d => d.Id == "DB0048");
        Assert.Contains("exceeding the 1048576-byte cap", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConcurrentCompilationsAcrossMultipleThreadsSucceedWithoutContention()
    {
        var compiler = new SynapseCompiler(ReferenceSet.FromDefaultContext());
        const int concurrency = 20;

        Parallel.For(0, concurrency, i =>
        {
            var sourceText = $$"""
                namespace Me.Budget{{i}};

                [State("state{{i}}")]
                public sealed record Spend{{i}}(decimal Spent = 0)
                {
                    public Spend{{i}}() : this(0) { }
                }
                """;
            var build = compiler.Compile(Source(sourceText, $"Me.Budget{i}"));
            Assert.True(build.Success, string.Join("\n", build.Diagnostics));
        });

        Assert.Equal(concurrency, compiler.Emits);
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class ThrowingAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Rule = new("TEST0001", "t", "t", "t", DiagnosticSeverity.Warning, true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxTreeAction(_ => throw new InvalidOperationException("boom"));
        }
    }
}
