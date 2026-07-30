using System.Collections.Immutable;
using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Manifest;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class InputUnionCompilation
{
    [Fact(DisplayName = "Root union lowers to stable contract identity, case ids, oneOf, and result schema")]
    public void RootUnionLowersToStableManifestContract()
    {
        var source = UnionProgram(
            """
            public sealed record ManualResearchRequest(string Prompt) : Synapse;
            public sealed record GmailMessageReceived(string MessageId) : Synapse;
            public union ResearchCompanyRequest(ManualResearchRequest, GmailMessageReceived);
            """);

        var result = Lower(source, "com.digitalbrain.research-company");

        Assert.True(result.Succeeded, result.Diagnostics);
        Assert.NotNull(result.Contract);
        Assert.Equal("com.digitalbrain.research-company", result.Contract!.BehaviorContractId);
        Assert.Equal(1, result.Contract.ContractMajorVersion);
        Assert.Equal("ResearchCompanyRequest", result.UnionName);
        Assert.Equal(
            ["case.GmailMessageReceived", "case.ManualResearchRequest"],
            result.CaseIds.Order(StringComparer.Ordinal));

        Assert.Equal(2, result.Contract.Cases.Count);
        Assert.Contains("\"oneOf\"", result.Contract.OneOfSchemaJson, StringComparison.Ordinal);
        Assert.Contains("case.ManualResearchRequest", result.Contract.OneOfSchemaJson, StringComparison.Ordinal);
        Assert.Contains("case.GmailMessageReceived", result.Contract.OneOfSchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("ResearchCompanyRequest", result.Contract.OneOfSchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Runtime.CompilerServices.IUnion", result.Contract.OneOfSchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain(", Version=", result.Contract.OneOfSchemaJson, StringComparison.Ordinal);

        var manual = result.Contract.Cases.Single(item => item.CaseId == "case.ManualResearchRequest");
        Assert.Equal(1, manual.CaseSchemaVersion);
        Assert.Equal("ManualResearchRequest", manual.CaseName);
        Assert.Contains("\"Prompt\"", manual.PayloadSchemaJson, StringComparison.Ordinal);
        Assert.Equal("""{"type":"object"}""", result.Contract.ResultSchemaJson);
        Assert.Contains("\"succeeded\":true", result.LoweringEvidenceJson, StringComparison.Ordinal);
        Assert.Contains("case.ManualResearchRequest", result.LoweringEvidenceJson, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Union case order does not change stable case ids or deterministic oneOf output")]
    public void UnionCaseOrderIsDeterministic()
    {
        var first = Lower(
            UnionProgram(
                """
                public sealed record AlphaRequest(string Value) : Synapse;
                public sealed record ZuluRequest(string Value) : Synapse;
                public union SampleRequest(AlphaRequest, ZuluRequest);
                """),
            "com.digitalbrain.order");
        var second = Lower(
            UnionProgram(
                """
                public sealed record AlphaRequest(string Value) : Synapse;
                public sealed record ZuluRequest(string Value) : Synapse;
                public union SampleRequest(ZuluRequest, AlphaRequest);
                """),
            "com.digitalbrain.order");

        Assert.True(first.Succeeded, first.Diagnostics);
        Assert.True(second.Succeeded, second.Diagnostics);
        Assert.Equal(first.CaseIds, second.CaseIds);
        Assert.Equal(first.Contract!.OneOfSchemaJson, second.Contract!.OneOfSchemaJson);
        Assert.Equal(
            first.Contract.Cases.Select(item => item.CaseId),
            second.Contract.Cases.Select(item => item.CaseId));
    }

    [Fact(DisplayName = "Compiler rejects default or null union cases")]
    public void RejectsDefaultOrNullUnionCases()
    {
        var result = Lower(
            UnionProgram(
                """
                public sealed record ManualResearchRequest(string Prompt) : Synapse;
                public union ResearchCompanyRequest(ManualResearchRequest, null);
                """),
            "com.digitalbrain.null-case");

        Assert.False(result.Succeeded);
        Assert.True(
            result.Diagnostics.Contains("default or null", StringComparison.OrdinalIgnoreCase)
            || result.Diagnostics.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase),
            result.Diagnostics);
    }

    [Fact(DisplayName = "Compiler rejects ambiguous duplicated union cases")]
    public void RejectsDuplicateUnionCases()
    {
        var result = Lower(
            UnionProgram(
                """
                public sealed record ManualResearchRequest(string Prompt) : Synapse;
                public union ResearchCompanyRequest(ManualResearchRequest, ManualResearchRequest);
                """),
            "com.digitalbrain.duplicate-case");

        Assert.False(result.Succeeded);
        Assert.Contains("ambiguous or duplicated", result.Diagnostics, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Compiler rejects nested unions")]
    public void RejectsNestedUnions()
    {
        var result = Lower(
            UnionProgram(
                """
                public sealed record ManualResearchRequest(string Prompt) : Synapse;
                public sealed record ScheduledTaskFired(string TaskId) : Synapse;
                public union NestedRequest(ManualResearchRequest, ScheduledTaskFired);
                public union ResearchCompanyRequest(NestedRequest, ManualResearchRequest);
                """),
            "com.digitalbrain.nested");

        Assert.False(result.Succeeded);
        Assert.True(
            result.Diagnostics.Contains("Nested unions", StringComparison.OrdinalIgnoreCase)
            || result.Diagnostics.Contains("more than one root input union", StringComparison.OrdinalIgnoreCase),
            result.Diagnostics);
    }

    [Fact(DisplayName = "Compiler rejects mutable non-record union payloads")]
    public void RejectsMutablePayloads()
    {
        var result = Lower(
            UnionProgram(
                """
                public sealed class MutableRequest
                {
                    public string Prompt { get; set; } = "";
                }

                public sealed record ManualResearchRequest(string Prompt) : Synapse;
                public union ResearchCompanyRequest(ManualResearchRequest, MutableRequest);
                """),
            "com.digitalbrain.mutable");

        Assert.False(result.Succeeded);
        Assert.Contains("immutable record", result.Diagnostics, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Compiler rejects more than one root input union")]
    public void RejectsMultipleRootUnions()
    {
        var result = Lower(
            UnionProgram(
                """
                public sealed record ManualResearchRequest(string Prompt) : Synapse;
                public sealed record ScheduledTaskFired(string TaskId) : Synapse;
                public union ResearchCompanyRequest(ManualResearchRequest, ScheduledTaskFired);
                public union AlternateRequest(ManualResearchRequest, ScheduledTaskFired);
                """),
            "com.digitalbrain.two-roots");

        Assert.False(result.Succeeded);
        Assert.Contains("more than one root input union", result.Diagnostics, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Contract-only compiler evidence carries policy and typed lowering facts for a root union")]
    public void CompilerEvidenceCarriesPolicyAndLoweringFacts()
    {
        var source = UnionProgram(
            """
            public sealed record ManualResearchRequest(string Prompt) : Synapse;
            public sealed record GmailMessageReceived(string MessageId) : Synapse;
            public union ResearchCompanyRequest(ManualResearchRequest, GmailMessageReceived);

            public sealed class SampleProgram : IBehaviorProgram<ManualResearchRequest>
            {
                public ValueTask ExecuteAsync(ManualResearchRequest trigger, IBehaviorContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }

            public sealed class SampleInstallTests : IBehaviorInstallTests
            {
                public ValueTask<BehaviorInstallTestReport> RunAsync(
                    IBehaviorContext context,
                    IReadOnlyDictionary<string, string> features,
                    CancellationToken cancellationToken)
                    => ValueTask.FromResult(BehaviorInstallTestReport.FromResults(
                    [
                        new BehaviorScenarioResult(
                            "scenario.install-gate-passes",
                            "install gate passes",
                            "bind.install-gate-passes",
                            true,
                            "green"),
                    ],
                    "green"));
            }
            """);

        var compile = new ContractOnlyBehaviorCompiler().Compile(source, new BehaviorId("com.digitalbrain.research-company"));
        Assert.True(compile.Succeeded, compile.Diagnostics);
        Assert.NotNull(compile.Contract);
        Assert.Equal("Preview", compile.Policy.LanguageVersion);
        Assert.Equal("contract-only-v1", compile.Policy.PolicyId);
        Assert.Contains("\"languageVersion\":\"Preview\"", compile.CompilerEvidenceJson, StringComparison.Ordinal);
        Assert.Contains("\"policy\":\"contract-only-v1\"", compile.CompilerEvidenceJson, StringComparison.Ordinal);
        Assert.Contains("case.ManualResearchRequest", compile.CompilerEvidenceJson, StringComparison.Ordinal);
        Assert.Contains("ResearchCompanyRequest", compile.CompilerEvidenceJson, StringComparison.Ordinal);
    }

    [Fact(DisplayName =
        "Non-union program with one immutable-record TTrigger lowers to a stable one-case contract")]
    public void SingleProgramTriggerLowersToStableOneCaseContract()
    {
        var source = UnionProgram(
            """
            public sealed record SampleTrigger(string Label) : Synapse;

            public sealed class SampleProgram : IBehaviorProgram<SampleTrigger>
            {
                public ValueTask ExecuteAsync(SampleTrigger trigger, IBehaviorContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }
            """);

        var result = Lower(source, "com.digitalbrain.sample");

        Assert.True(result.Succeeded, result.Diagnostics);
        Assert.NotNull(result.Contract);
        Assert.Equal("com.digitalbrain.sample", result.Contract!.BehaviorContractId);
        Assert.Equal(1, result.Contract.ContractMajorVersion);
        Assert.Equal(["case.SampleTrigger"], result.CaseIds);
        var single = Assert.Single(result.Contract.Cases);
        Assert.Equal("case.SampleTrigger", single.CaseId);
        Assert.Equal(1, single.CaseSchemaVersion);
        Assert.Equal("SampleTrigger", single.CaseName);
        Assert.Contains("\"Label\"", single.PayloadSchemaJson, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"string\"", single.PayloadSchemaJson, StringComparison.Ordinal);
        Assert.Equal("""{"type":"object"}""", result.Contract.ResultSchemaJson);

        Assert.Contains("\"oneOf\"", result.Contract.OneOfSchemaJson, StringComparison.Ordinal);
        Assert.Contains("case.SampleTrigger", result.Contract.OneOfSchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"oneOf\":[]", result.Contract.OneOfSchemaJson, StringComparison.Ordinal);
        Assert.Contains("\"succeeded\":true", result.LoweringEvidenceJson, StringComparison.Ordinal);
        Assert.Contains("case.SampleTrigger", result.LoweringEvidenceJson, StringComparison.Ordinal);

        var compile = new ContractOnlyBehaviorCompiler().Compile(source, new BehaviorId("com.digitalbrain.sample"));
        Assert.True(compile.Succeeded, compile.Diagnostics);
        Assert.NotNull(compile.Contract);
        Assert.Equal(result.Contract.OneOfSchemaJson, compile.Contract!.OneOfSchemaJson);
        Assert.Equal(result.Contract.Cases.Select(item => item.CaseId), compile.Contract.Cases.Select(item => item.CaseId));
    }

    [Fact(DisplayName = "Compiler rejects programs with no logical input synapse")]
    public void RejectsNoLogicalInput()
    {
        var result = Lower(
            UnionProgram(
                """
                public sealed record SampleTrigger(string Label) : Synapse;

                public sealed class SampleInstallTests : IBehaviorInstallTests
                {
                    public ValueTask<BehaviorInstallTestReport> RunAsync(
                        IBehaviorContext context,
                        IReadOnlyDictionary<string, string> features,
                        CancellationToken cancellationToken)
                        => ValueTask.FromResult(BehaviorInstallTestReport.FromResults(
                        [
                            new BehaviorScenarioResult(
                                "scenario.install-gate-passes",
                                "install gate passes",
                                "bind.install-gate-passes",
                                true,
                                "green"),
                        ],
                        "green"));
                }
                """),
            "com.digitalbrain.no-input");

        Assert.False(result.Succeeded);
        Assert.Contains("logical input", result.Diagnostics, StringComparison.OrdinalIgnoreCase);

        var compile = new ContractOnlyBehaviorCompiler().Compile(
            UnionProgram(
                """
                public sealed record SampleTrigger(string Label) : Synapse;
                """),
            new BehaviorId("com.digitalbrain.no-input"));
        Assert.False(compile.Succeeded);
        Assert.Contains("logical input", compile.Diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.Null(compile.Contract);
    }

    [Fact(DisplayName = "Compiler rejects more than one distinct root program trigger")]
    public void RejectsMultipleDistinctProgramTriggers()
    {
        var source = UnionProgram(
            """
            public sealed record SampleTrigger(string Label) : Synapse;
            public sealed record OtherTrigger(string Value) : Synapse;

            public sealed class SampleProgram : IBehaviorProgram<SampleTrigger>
            {
                public ValueTask ExecuteAsync(SampleTrigger trigger, IBehaviorContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }

            public sealed class OtherProgram : IBehaviorProgram<OtherTrigger>
            {
                public ValueTask ExecuteAsync(OtherTrigger trigger, IBehaviorContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }
            """);

        var result = Lower(source, "com.digitalbrain.two-triggers");

        Assert.False(result.Succeeded);
        Assert.Contains("more than one distinct program trigger", result.Diagnostics, StringComparison.OrdinalIgnoreCase);

        var compile = new ContractOnlyBehaviorCompiler().Compile(source, new BehaviorId("com.digitalbrain.two-triggers"));
        Assert.False(compile.Succeeded);
        Assert.Contains("more than one distinct program trigger", compile.Diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.Null(compile.Contract);
    }

    private static InputContractLoweringResult Lower(string source, string behaviorId)
        => BehaviorInputContractCompiler.Lower(
            source,
            new BehaviorId(behaviorId),
            BuildReferences());

    private static string UnionProgram(string body)
        => $$"""
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using DigitalBrain.Behaviors;

            {{body}}
            """;

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<MetadataReference>();

        void Add(Assembly assembly)
        {
            if (string.IsNullOrWhiteSpace(assembly.Location) || !set.Add(assembly.Location))
            {
                return;
            }

            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        Add(typeof(object).Assembly);
        Add(typeof(Enumerable).Assembly);
        Add(typeof(INeuron).Assembly);
        Add(typeof(IBehaviorProgram<>).Assembly);
        Add(Assembly.Load("System.Runtime"));
        Add(Assembly.Load("System.Collections"));
        Add(Assembly.Load("System.Linq"));
        Add(Assembly.Load("System.Private.CoreLib"));
        Add(Assembly.Load("netstandard"));
        Add(Assembly.Load("System.Threading"));
        Add(Assembly.Load("System.Threading.Tasks"));
        return [.. references];
    }
}
