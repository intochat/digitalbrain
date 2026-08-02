using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Manifest;
using DigitalBrain.Behaviors.Runtime;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class ScenarioBindingGate
{
    [Fact(DisplayName = "Feature scenarios derive stable ids, binding keys, and deterministic overview")]
    public void FeatureScenariosDeriveStableBindingsAndOverview()
    {
        const string feature =
            """
            Feature: research company
              Scenario: manual research succeeds
                Then research is stored
              Scenario: gmail research succeeds
                Then research is stored
            """;

        var scenarios = BehaviorScenarioBinder.DeriveScenarios(feature);
        Assert.Equal(2, scenarios.Count);
        Assert.Equal("scenario.manual-research-succeeds", scenarios[0].ScenarioId);
        Assert.Equal("bind.manual-research-succeeds", scenarios[0].BindingKey);
        Assert.Equal("scenario.gmail-research-succeeds", scenarios[1].ScenarioId);
        Assert.Equal(
            "Research company: gmail research succeeds; manual research succeeds",
            BehaviorScenarioBinder.ProjectOverview("Research company", scenarios));
    }

    [Fact(DisplayName = "Missing scenario binding is rejected with a reader-facing diagnostic")]
    public void MissingBindingIsRejected()
    {
        const string feature =
            """
            Feature: sample
              Scenario: alpha path
                Then alpha
              Scenario: zulu path
                Then zulu
            """;

        var declared = new[]
        {
            new BehaviorScenarioManifest("scenario.alpha-path", "alpha path", "bind.alpha-path"),
        };

        var result = BehaviorScenarioBinder.Bind(feature, declared);

        Assert.False(result.Passed);
        Assert.Contains("Missing scenario bindings", result.Detail, StringComparison.Ordinal);
        Assert.Contains("zulu path", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("password", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_key", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Duplicate Gherkin scenario titles are rejected")]
    public void DuplicateScenarioTitlesAreRejected()
    {
        const string feature =
            """
            Feature: sample
              Scenario: alpha path
                Then alpha
              Scenario: alpha path
                Then alpha again
            """;

        var result = BehaviorScenarioBinder.Bind(feature, []);

        Assert.False(result.Passed);
        Assert.Contains("Duplicate Gherkin scenario titles", result.Detail, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Orphaned bindings without Gherkin scenarios are rejected")]
    public void OrphanedBindingsAreRejected()
    {
        const string feature =
            """
            Feature: sample
              Scenario: alpha path
                Then alpha
            """;

        var declared = new[]
        {
            new BehaviorScenarioManifest("scenario.alpha-path", "alpha path", "bind.alpha-path"),
            new BehaviorScenarioManifest("scenario.ghost", "ghost path", "bind.ghost"),
        };

        var result = BehaviorScenarioBinder.Bind(feature, declared);

        Assert.False(result.Passed);
        Assert.Contains("Orphaned scenario bindings", result.Detail, StringComparison.Ordinal);
        Assert.Contains("ghost path", result.Detail, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Executable results must cover every scenario exactly once")]
    public void ExecutableResultsMustBeExhaustiveAndUnique()
    {
        const string feature =
            """
            Feature: sample
              Scenario: alpha path
                Then alpha
              Scenario: zulu path
                Then zulu
            """;

        var declared = BehaviorScenarioBinder.DeriveScenarios(feature);
        var missing = BehaviorScenarioBinder.Bind(
            feature,
            declared,
            [
                new BehaviorScenarioResult(
                    "scenario.alpha-path",
                    "alpha path",
                    "bind.alpha-path",
                    true,
                    "ok"),
            ]);
        Assert.False(missing.Passed);
        Assert.Contains("Missing executable result", missing.Detail, StringComparison.Ordinal);

        var duplicate = BehaviorScenarioBinder.Bind(
            feature,
            declared,
            [
                new BehaviorScenarioResult("scenario.alpha-path", "alpha path", "bind.alpha-path", true, "ok"),
                new BehaviorScenarioResult("scenario.alpha-path", "alpha path", "bind.alpha-path", true, "again"),
                new BehaviorScenarioResult("scenario.zulu-path", "zulu path", "bind.zulu-path", true, "ok"),
            ]);
        Assert.False(duplicate.Passed);
        Assert.Contains("Duplicate executable result", duplicate.Detail, StringComparison.Ordinal);

        var failed = BehaviorScenarioBinder.Bind(
            feature,
            declared,
            [
                new BehaviorScenarioResult("scenario.alpha-path", "alpha path", "bind.alpha-path", true, "ok"),
                new BehaviorScenarioResult("scenario.zulu-path", "zulu path", "bind.zulu-path", false, "assertion failed"),
            ]);
        Assert.False(failed.Passed);
        Assert.Contains("Scenario failures", failed.Detail, StringComparison.Ordinal);
        Assert.Contains("zulu path", failed.Detail, StringComparison.Ordinal);

        var passed = BehaviorScenarioBinder.Bind(
            feature,
            declared,
            [
                new BehaviorScenarioResult("scenario.alpha-path", "alpha path", "bind.alpha-path", true, "ok"),
                new BehaviorScenarioResult("scenario.zulu-path", "zulu path", "bind.zulu-path", true, "ok"),
            ]);
        Assert.True(passed.Passed, passed.Detail);
        Assert.Equal(2, passed.ScenarioCount);
    }

    [Fact(DisplayName = "BDD gate rejects a feature with no scenarios before loading authored tests")]
    public void BddGateRejectsEmptyFeature()
    {
        var gate = new InstallTestsBddGate();
        var envelope = new DigitalBrain.Behaviors.Artifacts.BehaviorArtifactEnvelope(
            new BehaviorDefinitionManifest(
                new BehaviorId("com.digitalbrain.sample"),
                "Sample",
                "Sample",
                new BehaviorEntryPoints(
                    [],
                    new BehaviorContractManifest(
                        "com.digitalbrain.sample",
                        1,
                        """{"oneOf":[]}""",
                        [],
                        """{"type":"object"}""")),
                [],
                "Sample",
                BehaviorInputContractCompiler.DefaultPolicy,
                [],
                new BehaviorResourceLimits(1, 1, 1)),
            "public sealed class Sample { }",
            "Feature: sample\n",
            """{"libraries":{},"version":1}""",
            ReadOnlyMemory<byte>.Empty,
            """{"runtimeTarget":{"name":"net11.0"}}""",
            """{"succeeded":true}""",
            """{"policy":"v1","result":"pending"}""",
            """{"passed":false,"scenarios":0}""");

        var report = gate.Evaluate(
            envelope,
            ReadOnlyMemory<byte>.Empty,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            new StaticCapabilities(),
            TimeProvider.System);

        Assert.False(report.Passed);
        Assert.Contains("at least one Gherkin Scenario", report.Detail, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BDD gate rejects a nominally passing report with zero executable results")]
    public void BddGateRejectsPassingReportWithEmptyResults()
    {
        const string feature =
            """
            Feature: sample behavior
              Scenario: install gate passes
                Then the install gate passes
            """;

        var program =
            """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using DigitalBrain.Behaviors;

            public sealed record SampleTrigger(string Label) : Synapse;

            public sealed class SampleProgram : IBehaviorProgram<SampleTrigger>
            {
                public ValueTask ExecuteAsync(SampleTrigger trigger, IBehaviorContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }

            public sealed class SampleInstallTests : IBehaviorInstallTests
            {
                public ValueTask<BehaviorInstallTestReport> RunAsync(
                    IBehaviorContext context,
                    IReadOnlyDictionary<string, string> features,
                    CancellationToken cancellationToken)
                    => ValueTask.FromResult(new BehaviorInstallTestReport(true, 1, "legacy empty", Array.Empty<BehaviorScenarioResult>()));
            }
            """;

        var compile = new ContractOnlyBehaviorCompiler().Compile(program, new BehaviorId("com.digitalbrain.sample"));
        Assert.True(compile.Succeeded, compile.Diagnostics);

        var scenarios = BehaviorScenarioBinder.DeriveScenarios(feature);
        var envelope = new DigitalBrain.Behaviors.Artifacts.BehaviorArtifactEnvelope(
            new BehaviorDefinitionManifest(
                new BehaviorId("com.digitalbrain.sample"),
                "Sample",
                "Sample",
                new BehaviorEntryPoints(
                    [],
                    new BehaviorContractManifest(
                        "com.digitalbrain.sample",
                        1,
                        """{"oneOf":[]}""",
                        [],
                        """{"type":"object"}""")),
                scenarios,
                "Sample",
                BehaviorInputContractCompiler.DefaultPolicy,
                [],
                new BehaviorResourceLimits(1, 1, 1)),
            program,
            feature,
            """{"libraries":{},"version":1}""",
            compile.AssemblyBytes,
            """{"runtimeTarget":{"name":"net11.0"}}""",
            compile.CompilerEvidenceJson,
            """{"policy":"v1","result":"pending"}""",
            """{"passed":false,"scenarios":1}""");

        var report = new InstallTestsBddGate().Evaluate(
            envelope,
            compile.AssemblyBytes,
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            new StaticCapabilities(),
            TimeProvider.System);

        Assert.False(report.Passed);
        Assert.Contains("Every Gherkin scenario requires one executable result", report.Detail, StringComparison.Ordinal);
    }

    private sealed class StaticCapabilities : IBehaviorCapabilityResolver
    {
        public TNeuron Get<TNeuron>(string name)
            where TNeuron : class, INeuron
            => throw new InvalidOperationException("Capabilities are unavailable in this unit test.");
    }
}
