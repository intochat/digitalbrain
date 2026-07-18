using Core;
using Core.AI;
using IAW.Testing;
using Ino.Core;
using Ino.Core.Capabilities;
using Ino.Core.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace Ino.Core.Hosting.Tests.Fixtures;

/// <summary>
/// Shared Orleans TestCluster for InoNeuron grain tests. Wires the full
/// IAW substrate (streaming, durable jobs, attribute mappers) plus an in-process
/// <see cref="StubCortexCapability"/> whose response is settable per test.
///
/// One cluster per test run — tests must set <see cref="StubCortex"/>.NextResult
/// before calling the grain, and clear it after if isolation matters.
/// </summary>
public sealed class InoNeuronTestSiloFixture : IAsyncLifetime
{
    // Static so InoNeuronSiloConfigurator (new()-constructed by Orleans) can reach it.
    // Static slot — only one InoNeuronTestSiloFixture may be active per test process.
    // xUnit [CollectionFixture] guarantees this; not safe for parallel test assemblies.
    internal static StubCortexCapability? ActiveStub;

    public TestCluster Cluster { get; private set; } = null!;

    public StubCortexCapability StubCortex { get; } = new();

    public async ValueTask InitializeAsync()
    {
        ActiveStub = StubCortex;

        var builder = new TestClusterBuilder { Options = { InitialSilosCount = 1 } };
        builder.AddSiloBuilderConfigurator<InoNeuronSiloConfigurator>();
        builder.AddClientBuilderConfigurator<InoNeuronClientConfigurator>();

        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        ActiveStub = null;
        if (Cluster is null) return;
        try { await Cluster.StopAllSilosAsync(); }
        finally { await Cluster.DisposeAsync(); }
    }
}

/// <summary>
/// Controllable test double for <see cref="ICortexCapability"/>. Tests set
/// <see cref="NextResult"/> before invoking the grain; the grain's
/// <see cref="RouteAsync"/> call returns that result.
/// </summary>
public sealed class StubCortexCapability : ICortexCapability
{
    public RoutingResult? NextResult { get; set; }
    public string? LastPrompt { get; private set; }

    public Task<RoutingResult> RouteAsync(string prompt, NeuronContext ctx, CancellationToken ct)
    {
        LastPrompt = prompt;
        return Task.FromResult(
            NextResult ?? new RoutingResult(NeuronResult.Ok("stub default"), RoutingSource.Unrouted, null));
    }
}

public sealed class InoNeuronSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorage("Default")
            .AddMemoryGrainStorage("PubSubStore")
            .AddMemoryStreams(IAWConstants.StreamProvider)
            .UseInMemoryReminderService()
            .UseInMemoryDurableJobs();

        siloBuilder.AddBroadcastChannel(IAWConstants.UIBroadcastProvider);

        siloBuilder.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
        siloBuilder.AddStateMachineStorage();

        var mockChatClient = new MockChatClient().ReturnsText("mock");
        LlmAttributeMapperRegistration.RegisterAllAttributeMappers(siloBuilder.Services, mockChatClient);
        siloBuilder.Services.AddSingleton<IChatClient>(mockChatClient);

        siloBuilder.Services.AddSingleton<IFirePort, NoOpFirePort>();
        siloBuilder.Services.AddSingleton<ICortexCapability>(
            _ => InoNeuronTestSiloFixture.ActiveStub ?? new StubCortexCapability());
    }
}

public sealed class InoNeuronClientConfigurator : IClientBuilderConfigurator
{
    public void Configure(
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        IClientBuilder clientBuilder)
    {
        clientBuilder.AddMemoryStreams(IAWConstants.StreamProvider);
    }
}
