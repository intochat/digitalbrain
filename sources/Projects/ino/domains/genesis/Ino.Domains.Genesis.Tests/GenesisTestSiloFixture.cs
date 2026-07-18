using System.Diagnostics;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Placement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Runtime.MembershipService.SiloMetadata;
using Orleans.TestingHost;
using Xunit;

namespace Ino.Domains.Genesis.Tests;

/// <summary>
/// Single-silo test cluster wired for the Phase 4 Slice E.2 L1 loop. The
/// TestCluster's lone silo is tagged with <c>ino.silo=kernel</c> so the
/// kernel-pinned <c>Discovery</c> grain (<c>[PinToSilo("kernel")]</c>)
/// activates here; <c>CreatorNeuron</c>, <c>RoslynPlan</c> and
/// <c>NeuronRegistry</c> aren't pinned, so default placement converges
/// them on the same lone silo. <see cref="IFirePort"/> is registered as a
/// no-op stand-in — the acceptance scenario drives <c>CreatorNeuron</c>
/// directly via grain-method calls and verifies post-state, so the
/// broadcast plumbing is tested separately by <c>MissedIntentTrackerTests</c>.
///
/// Inherits from <see cref="IAsyncLifetime"/> so xunit.v3 can wire it as an
/// <see cref="ICollectionFixture{T}"/> via <see cref="InoTestCollection"/>.
/// </summary>
public sealed class GenesisTestSiloFixture : IAsyncLifetime
{
    public TestCluster Cluster { get; private set; } = null!;
    public IGrainFactory Grains => Cluster.Client;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder { Options = { InitialSilosCount = 1 } };
        builder.AddSiloBuilderConfigurator<GenesisTestSiloConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Cluster is null) return;
        try { await Cluster.StopAllSilosAsync(); }
        finally { await Cluster.DisposeAsync(); }
    }
}

internal sealed class GenesisTestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder silo)
    {
        // Volatile journaling — same shape as the shared TestSiloConfigurator.
        // Re-declared here because we can't inherit + extend, only swap.
        silo.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
        silo.AddStateMachineStorage();

        // PinToSilo placement: tag the lone silo as "kernel" so Discovery's
        // [PinToSilo("kernel")] director can find a candidate. Genesis grains
        // aren't pinned, so they place on the same silo by default.
        silo.UseSiloMetadata(new Dictionary<string, string>
        {
            [PinToSiloStrategy.SiloMetadataKey] = "kernel",
        });
        silo.Services.AddPinToSiloPlacement();

        // Discovery / FirePort dependencies. CapabilityEnforcer is permissive
        // (no domain capabilities declared in tests). FirePort is constructed
        // but the tests don't drive broadcast end-to-end — they call
        // CreatorNeuron.ReactAsync directly — so this is mostly a constructor
        // safety net so DI can build any grain that takes IFirePort.
        silo.Services.AddSingleton<ICapabilityEnforcer>(_ =>
            new CapabilityEnforcer(new Dictionary<DomainId, IReadOnlyList<Capability>>()));
        silo.Services.AddSingleton(_ => new ActivitySource("Ino.Domains.Genesis.Tests"));
        silo.Services.AddSingleton<IDiscoveryClient, DiscoveryClient>();
        silo.Services.AddSingleton<IFirePort, FirePort>();
    }
}
