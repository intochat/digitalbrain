using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Manifest;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class DirectedCapabilityGrants
{
    [Fact(DisplayName = "Compiler derives a named target edge from Get + typed SendAsync")]
    public void DerivesNamedTargetEdgeFromGetAndTypedSendAsync()
    {
        var result = Compile(NamedTypedSendProgram());

        Assert.True(result.Succeeded, result.Diagnostics);
        var grant = Assert.Single(result.CapabilityGrants);
        Assert.Equal("test.gmail", grant.TargetNeuronContractId);
        Assert.Equal("test.gmail-request", grant.AcceptedRequestSynapseId);
        Assert.Equal(1, grant.AcceptedRequestSchemaVersion);
        Assert.Equal("test.gmail-response", grant.EmittedResultSynapseId);
        Assert.Equal(1, grant.EmittedResultSchemaVersion);
        Assert.Equal("named", grant.TargetInstancePolicy);
        Assert.Equal("work", grant.TargetInstanceName);
        Assert.DoesNotContain("MethodAlias", grant.GetType().GetProperties().Select(property => property.Name));
        Assert.DoesNotContain("ReadMessage", grant.ToString(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Compiler derives a one-way SendAsync edge with no result synapse")]
    public void DerivesOneWaySendAsyncEdge()
    {
        var result = Compile(OneWaySendProgram());

        Assert.True(result.Succeeded, result.Diagnostics);
        var grant = Assert.Single(result.CapabilityGrants);
        Assert.Equal("test.notify", grant.TargetNeuronContractId);
        Assert.Equal("test.notify-ping", grant.AcceptedRequestSynapseId);
        Assert.Equal(1, grant.AcceptedRequestSchemaVersion);
        Assert.Null(grant.EmittedResultSynapseId);
        Assert.Null(grant.EmittedResultSchemaVersion);
        Assert.Equal("default", grant.TargetInstancePolicy);
        Assert.Equal("default", grant.TargetInstanceName);
    }

    [Fact(DisplayName = "Derived grants keep stable contract IDs through formatting and local renames")]
    public void SemanticDerivationSurvivesFormattingAndLocalRenames()
    {
        var first = Compile(NamedTypedSendProgram());
        var second = Compile(NamedTypedSendProgramRenamed());

        Assert.True(first.Succeeded, first.Diagnostics);
        Assert.True(second.Succeeded, second.Diagnostics);
        Assert.Equal(first.CapabilityGrants, second.CapabilityGrants);
    }

    [Fact(DisplayName = "Compiler rejects non-BehaviorBrain Get lookalikes")]
    public void RejectsNonBehaviorBrainLookalikes()
    {
        var result = Compile(LookalikeGetProgram());

        Assert.False(result.Succeeded);
        Assert.Contains("BehaviorBrain", result.Diagnostics, StringComparison.Ordinal);
        Assert.Empty(result.CapabilityGrants);
    }

    [Fact(DisplayName = "Admission rejects an undeclared directed edge")]
    public void AdmissionRejectsUndeclaredEdge()
    {
        var catalog = ActiveCatalog();
        var grants = new[]
        {
            new BehaviorCapabilityGrant(
                "test.missing-neuron",
                "test.gmail-request",
                1,
                "test.gmail-response",
                1,
                "default",
                "default"),
        };

        var admission = BehaviorContractCompatibility.AdmitCapabilityGrants(grants, catalog);

        Assert.False(admission.IsAdmitted);
        Assert.Contains("undeclared", admission.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Admission rejects an inactive module neuron")]
    public void AdmissionRejectsInactiveModule()
    {
        var catalog = ActiveCapabilityCatalog.Create([new CatalogModule(
            new ModuleId("catalog.active"),
            ActiveManifest())]);
        var grants = new[]
        {
            new BehaviorCapabilityGrant(
                "test.inactive",
                "test.inactive-request",
                1,
                "test.inactive-response",
                1,
                "default",
                "default"),
        };

        var admission = BehaviorContractCompatibility.AdmitCapabilityGrants(grants, catalog);

        Assert.False(admission.IsAdmitted);
        Assert.Contains("inactive", admission.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Admission rejects incompatible request synapse version")]
    public void AdmissionRejectsIncompatibleRequestVersion()
    {
        var catalog = ActiveCatalog();
        var grants = new[]
        {
            new BehaviorCapabilityGrant(
                "test.gmail",
                "test.gmail-request",
                2,
                "test.gmail-response",
                1,
                "named",
                "work"),
        };

        var admission = BehaviorContractCompatibility.AdmitCapabilityGrants(grants, catalog);

        Assert.False(admission.IsAdmitted);
        Assert.Contains("version", admission.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Admission rejects widened or incompatible result synapse")]
    public void AdmissionRejectsWidenedResultType()
    {
        var catalog = ActiveCatalog();
        var grants = new[]
        {
            new BehaviorCapabilityGrant(
                "test.gmail",
                "test.gmail-request",
                1,
                "test.widened-response",
                1,
                "named",
                "work"),
        };

        var admission = BehaviorContractCompatibility.AdmitCapabilityGrants(grants, catalog);

        Assert.False(admission.IsAdmitted);
        Assert.Contains("result", admission.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Admission rejects legacy method-alias grant shape")]
    public void AdmissionRejectsLegacyMethodAliasGrant()
    {
        var catalog = ActiveCatalog();
        var legacy = CreateLegacyMethodAliasGrant();

        var admission = BehaviorContractCompatibility.AdmitCapabilityGrants([legacy], catalog);

        Assert.False(admission.IsAdmitted);
        Assert.Contains("method", admission.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Null(typeof(BehaviorCapabilityGrant).GetProperty("MethodAlias"));
        Assert.Null(typeof(BehaviorCapabilityGrant).GetProperty("ContractAlias"));
    }

    [Fact(DisplayName = "Admission accepts a derived edge present in the active catalog")]
    public void AdmissionAcceptsDerivedCatalogEdge()
    {
        var compile = Compile(NamedTypedSendProgram());
        Assert.True(compile.Succeeded, compile.Diagnostics);

        var admission = BehaviorContractCompatibility.AdmitCapabilityGrants(
            compile.CapabilityGrants,
            ActiveCatalog());

        Assert.True(admission.IsAdmitted, admission.Detail);
        Assert.Equal(compile.CapabilityGrants, admission.Grants);
    }

    [Fact(DisplayName = "Derived grants persist only directed edge identity, never method aliases")]
    public void PersistedGrantSchemaIsDirectedEdgeOnly()
    {
        var result = Compile(NamedTypedSendProgram());
        Assert.True(result.Succeeded, result.Diagnostics);
        var grant = Assert.Single(result.CapabilityGrants);

        var names = grant.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "AcceptedRequestSchemaVersion",
                "AcceptedRequestSynapseId",
                "EmittedResultSchemaVersion",
                "EmittedResultSynapseId",
                "TargetInstanceName",
                "TargetInstancePolicy",
                "TargetNeuronContractId",
            ],
            names);
        Assert.DoesNotContain(names, name => name.Contains("Method", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Alias", StringComparison.OrdinalIgnoreCase));
    }

    private static BehaviorCompileResult Compile(string program)
        => new ContractOnlyBehaviorCompiler().Compile(program, new BehaviorId("com.digitalbrain.grants"));

    private static ActiveCapabilityCatalog ActiveCatalog()
        => ActiveCapabilityCatalog.Create(
        [
            new CatalogModule(new ModuleId("catalog.gmail"), ActiveManifest()),
            new CatalogModule(new ModuleId("catalog.notify"), NotifyManifest()),
        ]);

    private static CapabilityManifest ActiveManifest()
        => new(
            new ModuleId("catalog.gmail"),
            "1.0.0",
            "Gmail catalog",
            [],
            [
                new NeuronCapabilityDescriptor(
                    "test.gmail",
                    "Test gmail neuron",
                    "default",
                    [
                        new SynapseCapabilityDescriptor(
                            "test.gmail-request",
                            1,
                            "Gmail request",
                            """{"type":"object","properties":{"Prompt":{"type":"string"}}}""",
                            []),
                    ],
                    [
                        new SynapseCapabilityDescriptor(
                            "test.gmail-response",
                            1,
                            "Gmail response",
                            """{"type":"object","properties":{"Status":{"type":"string"}}}""",
                            []),
                    ]),
            ]);

    private static CapabilityManifest NotifyManifest()
        => new(
            new ModuleId("catalog.notify"),
            "1.0.0",
            "Notify catalog",
            [],
            [
                new NeuronCapabilityDescriptor(
                    "test.notify",
                    "Notify neuron",
                    "default",
                    [
                        new SynapseCapabilityDescriptor(
                            "test.notify-ping",
                            1,
                            "Notify ping",
                            """{"type":"object","properties":{"Message":{"type":"string"}}}""",
                            []),
                    ],
                    []),
            ]);

    private static BehaviorCapabilityGrant CreateLegacyMethodAliasGrant()
    {
        // Legacy authority was method-shaped: contract + method alias + target instance.
        // Construct via reflection when the directed shape is the only public constructor,
        // so admission still rejects method-alias authority explicitly.
        var type = typeof(BehaviorCapabilityGrant);
        var methodAlias = type.GetProperty("MethodAlias");
        if (methodAlias is not null)
        {
            return (BehaviorCapabilityGrant)Activator.CreateInstance(
                type,
                "test.gmail",
                "ReadMessage",
                "work")!;
        }

        return new BehaviorCapabilityGrant(
            "test.gmail",
            "ReadMessage",
            1,
            null,
            null,
            "method-alias",
            "work");
    }

    private static string NamedTypedSendProgram()
        => """
            using System.Collections.Generic;
            using System.ComponentModel;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using DigitalBrain.Behaviors;
            using Orleans;

            [Alias("test.research-trigger")]
            public sealed record ResearchTrigger(string Prompt) : Synapse;

            [Alias("test.gmail")]
            [Description("Test gmail neuron")]
            public interface IGmail : INeuron;

            [Alias("test.gmail-response")]
            [Description("Gmail response")]
            public sealed record GmailResponse(string Status) : Synapse;

            [Alias("test.gmail-request")]
            [Description("Gmail request")]
            public sealed record GmailRequest(string Prompt) : RequestSynapse<GmailResponse>;

            public sealed class SampleProgram : IBehaviorProgram<ResearchTrigger>
            {
                public ValueTask ExecuteAsync(ResearchTrigger trigger, IBehaviorContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }

            public static class BehaviorEntry
            {
                public static async Task RunAsync(BehaviorBrain<ResearchTrigger> brain)
                {
                    var gmail = brain.Get<IGmail>("work");
                    var result = await gmail.SendAsync(new GmailRequest(brain.Trigger.Prompt));
                }
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
            """;

    private static string NamedTypedSendProgramRenamed()
        => """
            using System.Collections.Generic;
            using System.ComponentModel;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using DigitalBrain.Behaviors;
            using Orleans;

            [Alias("test.research-trigger")]
            public sealed record ResearchTrigger(string Prompt) : Synapse;

            [Alias("test.gmail")]
            [Description("Test gmail neuron")]
            public interface IGmail : INeuron;

            [Alias("test.gmail-response")]
            [Description("Gmail response")]
            public sealed record GmailResponse(string Status) : Synapse;

            [Alias("test.gmail-request")]
            [Description("Gmail request")]
            public sealed record GmailRequest(string Prompt) : RequestSynapse<GmailResponse>;

            public sealed class SampleProgram : IBehaviorProgram<ResearchTrigger>
            {
                public ValueTask ExecuteAsync(ResearchTrigger trigger, IBehaviorContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }

            public static class BehaviorEntry
            {
                public static async Task RunAsync(BehaviorBrain<ResearchTrigger> brain)
                {
                    var neuronRef   = brain.Get<IGmail>( "work" );
                    var response = await neuronRef.SendAsync(  new GmailRequest( brain.Trigger.Prompt )  );
                    _ = response;
                }
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
            """;

    private static string OneWaySendProgram()
        => """
            using System.Collections.Generic;
            using System.ComponentModel;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using DigitalBrain.Behaviors;
            using Orleans;

            [Alias("test.research-trigger")]
            public sealed record ResearchTrigger(string Prompt) : Synapse;

            [Alias("test.notify")]
            [Description("Notify neuron")]
            public interface INotify : INeuron;

            [Alias("test.notify-ping")]
            [Description("Notify ping")]
            public sealed record NotifyPing(string Message) : Synapse;

            public sealed class SampleProgram : IBehaviorProgram<ResearchTrigger>
            {
                public ValueTask ExecuteAsync(ResearchTrigger trigger, IBehaviorContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }

            public static class BehaviorEntry
            {
                public static async Task RunAsync(BehaviorBrain<ResearchTrigger> brain)
                {
                    var notify = brain.Get<INotify>();
                    await notify.SendAsync(new NotifyPing(brain.Trigger.Prompt));
                }
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
            """;

    private static string LookalikeGetProgram()
        => """
            using System.Collections.Generic;
            using System.ComponentModel;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using DigitalBrain.Behaviors;
            using Orleans;

            [Alias("test.research-trigger")]
            public sealed record ResearchTrigger(string Prompt) : Synapse;

            [Alias("test.gmail")]
            [Description("Test gmail neuron")]
            public interface IGmail : INeuron;

            [Alias("test.gmail-response")]
            [Description("Gmail response")]
            public sealed record GmailResponse(string Status) : Synapse;

            [Alias("test.gmail-request")]
            [Description("Gmail request")]
            public sealed record GmailRequest(string Prompt) : RequestSynapse<GmailResponse>;

            public sealed class SampleProgram : IBehaviorProgram<ResearchTrigger>
            {
                public ValueTask ExecuteAsync(ResearchTrigger trigger, IBehaviorContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }

            public sealed class FakeBrain
            {
                public FakeNeuronReference<TNeuron> Get<TNeuron>(string name = "default")
                    where TNeuron : INeuron
                    => new(name);
            }

            public readonly struct FakeNeuronReference<TNeuron>
                where TNeuron : INeuron
            {
                private readonly string _name;
                public FakeNeuronReference(string name) => _name = name;
                public Task SendAsync(Synapse synapse) => Task.CompletedTask;
                public Task<TResponse> SendAsync<TResponse>(RequestSynapse<TResponse> request)
                    where TResponse : Synapse
                    => Task.FromResult(default(TResponse)!);
            }

            public static class BehaviorEntry
            {
                public static async Task RunAsync()
                {
                    var brain = new FakeBrain();
                    var gmail = brain.Get<IGmail>("work");
                    await gmail.SendAsync(new GmailRequest("x"));
                }
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
            """;

    private sealed class CatalogModule(ModuleId id, CapabilityManifest capabilities) : ICompiledModule
    {
        public ModuleId Id { get; } = id;

        public CapabilityManifest Capabilities { get; } = capabilities;

        public void PrepareSerialization(IServiceCollection services)
        {
        }

        public void Activate(ISiloBuilder builder)
        {
        }
    }
}
