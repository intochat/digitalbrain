using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Brain;
using Ino.Core.Hosting.Llm;
using Ino.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace Ino.Domains.Travel.Tests.Storyboard;

// Single-silo TestCluster for storyboard behavioural assertions.
// Wires:
//   - VolatileStateMachineStorageProvider (DurableGrain storage)
//   - BrainTraceFilter + BrainPulseHub (brain stream observability)
//   - BddMockChatClientFactory + INeuronPromptCorpus (routing corpus)
//   - IDiscoveryClient, IFirePort, ICortexCapability (for IInoNeuron.AskAsync)
//
// NOTE: Discovery grain is [PinToSilo("kernel")] and requires a silo tagged
// with "ino.silo=kernel" metadata. TestCluster has no such tag, so placement
// will fail when CortexCapability.RouteAsync calls discovery.DumpNeuronsAsync().
// This is the documented reason the Cortex→PlanTrip hop cannot be asserted in
// this cluster. Slice 2 will wire a stub Discovery that returns pre-seeded
// neuron registrations without the silo-metadata constraint.
//
// Lifecycle: managed as a test-run singleton via StoryboardFixtureHooks
// [BeforeTestRun] / [AfterTestRun]. A single cluster spans all Tokyo
// scenarios so the 5-10s startup cost is paid once per test run.
public sealed class StoryboardTestSiloFixture : IAsyncLifetime
{
    // The hub is created in InitializeAsync and passed into the silo DI via
    // the static field (configurators run synchronously during DeployAsync,
    // inside the same process, so a lock-guarded static is safe).
    private static BrainPulseHub? pendingHub;
    private static readonly object PendingLock = new();

    public TestCluster Cluster { get; private set; } = null!;
    public IGrainFactory Grains => Cluster.Client;
    public BrainPulseHub PulseHub { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var hub = new BrainPulseHub();
        lock (PendingLock) pendingHub = hub;

        try
        {
            var builder = new TestClusterBuilder { Options = { InitialSilosCount = 1 } };
            builder.AddSiloBuilderConfigurator<StoryboardSiloConfigurator>();
            Cluster = builder.Build();
            await Cluster.DeployAsync();
        }
        finally
        {
            lock (PendingLock) pendingHub = null;
        }

        PulseHub = hub;
    }

    public async ValueTask DisposeAsync()
    {
        if (Cluster is null) return;
        try { await Cluster.StopAllSilosAsync(); }
        finally { await Cluster.DisposeAsync(); }
    }

    internal static BrainPulseHub ClaimHub() =>
        pendingHub ?? throw new InvalidOperationException(
            "ClaimHub() called outside a Deploy window; pendingHub was null.");

    private sealed class StoryboardSiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder silo)
        {
            silo.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
            silo.AddStateMachineStorage();

            // Brain stream — register the pre-created hub so both the silo's
            // BrainTraceFilter and the fixture's StoryboardCapture share one channel.
            var hub = ClaimHub();
            silo.AddIncomingGrainCallFilter<InoInstanceContextFilter>();
            silo.AddIncomingGrainCallFilter<BrainTraceFilter>();
            silo.Services.AddSingleton(hub);
            silo.Services.AddSingleton<IBrainPulseSink>(hub);

            // BDD mock LLM. Load scenarios from the test bin's Features/ dir
            // (travel domain copies its .feature files there via <Content>).
            var featuresDir = Path.Combine(AppContext.BaseDirectory, "Features");
            var loadResult = BddScenarioLoader.LoadFromDirectories(new[] { featuresDir });

            silo.Services.AddSingleton<IReasoningProbe, InMemoryReasoningProbe>();
            silo.Services.AddSingleton<IReadOnlyList<BddScenario>>(loadResult.Scenarios);
            silo.Services.AddSingleton<INeuronPromptCorpus>(sp =>
                new BddScenarioPromptCorpus(sp.GetRequiredService<IReadOnlyList<BddScenario>>()));

            silo.Services.AddSingleton<IChatClientFactory>(sp =>
            {
                var probe = sp.GetRequiredService<IReasoningProbe>();
                var log = sp.GetService<ILoggerFactory>()?.CreateLogger<BddMockChatClient>();
                return new BddMockChatClientFactory(loadResult.Scenarios, probe, log);
            });

            // Non-keyed IChatClient (InoNeuron + CortexCapability use this).
            silo.Services.AddSingleton<IChatClient>(sp =>
                sp.GetRequiredService<IChatClientFactory>().ForTier(LlmTier.Balanced));

            // Keyed IChatClient per tier (TripPlannerNeuron uses chatFactory.ForTier).
            foreach (var tier in new[] { LlmTier.Fast, LlmTier.Balanced, LlmTier.Reasoning })
            {
                var t = tier;
                silo.Services.AddKeyedSingleton<IChatClient>(t, (sp, _) =>
                    sp.GetRequiredService<IChatClientFactory>().ForTier(t));
            }

            // DiscoveryClient + NoOpFirePort + ICortexCapability. Discovery calls
            // will fail at placement (no "kernel" silo), but CortexCapability and
            // FirePort are tolerant of that failure path.
            silo.Services.AddSingleton<IDiscoveryClient, DiscoveryClient>();
            silo.Services.AddSingleton<IFirePort, NoOpFirePort>();
            silo.Services.AddInoNeuron();
        }
    }
}
