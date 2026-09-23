using DigitalBrain.Compute;
using DigitalBrain.Connections;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;
using DigitalBrain.MyData;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Connections.Tests.Unit;

public sealed class ConnectFlowFacts
{
    private const string Owner = "owner-1";
    private const string Canary = "canary-connection-4b8e12";

    [Fact]
    public async Task ConnectingStoresTheCredentialAsASecretReferenceAndProbes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create()
            .WithModule<MyDataModule>()
            .WithModule<ComputeModule>()
            .WithModule<ConnectionsModule>()
            .StartAsync(ct);
        var connections = brain.Get<IConnections>(Owner);
        var vault = brain.Get<IVault>(Owner);

        var record = await connections.Connect(new ConnectConnection
        {
            Source = ConnectionSources.WebResearch,
            ConnectionId = "research",
            Label = "Research key",
            Value = Canary,
        }, UserCaller(), ct);

        Assert.Equal(ConnectionStatus.Connected, record.Status);
        Assert.Equal(ConnectionSources.WebResearch, record.Source);
        Assert.StartsWith("secret://", record.Credential.Reference, StringComparison.Ordinal);
        Assert.Equal(Owner, record.WorkspaceId);

        var list = await connections.List(ct);
        Assert.Single(list);

        var export = await vault.Export(UserCaller(), ct);
        var text = string.Join("\n", export.Fields.Select(field => $"{field.FieldPath}={field.Value}"));
        Assert.DoesNotContain(Canary, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProbeTracksExpiredThenFailingStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var scripted = new ScriptedProbe(ConnectionProbeOutcome.Expired, ConnectionProbeOutcome.Failing);
        await using var brain = await UnitTest.Create()
            .WithModule<MyDataModule>()
            .WithModule<ComputeModule>()
            .WithModule<ConnectionsModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IConnectionProbe>(scripted))
            .StartAsync(ct);
        var connections = brain.Get<IConnections>(Owner);

        var connected = await connections.Connect(new ConnectConnection
        {
            Source = ConnectionSources.Supabase,
            ConnectionId = "db",
            Value = "Host=db;Database=app;Username=reader;Password=pw",
        }, UserCaller(), ct);
        Assert.Equal(ConnectionStatus.Expired, connected.Status);

        var probed = await connections.Probe("db", UserCaller(), ct);
        Assert.Equal(ConnectionStatus.Failing, probed.Status);
        Assert.NotNull(probed.LastProbedAt);
    }

    [Fact]
    public async Task DisconnectRemovesTheConnectionAndPublishesASignal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create()
            .WithModule<MyDataModule>()
            .WithModule<ComputeModule>()
            .WithModule<ConnectionsModule>()
            .StartAsync(ct);
        var connections = brain.Get<IConnections>(Owner);
        await using var disconnected = await brain.Observe<ConnectionDisconnected>(connections, ct);

        await connections.Connect(new ConnectConnection
        {
            Source = ConnectionSources.Gmail,
            ConnectionId = "mail",
            Value = "oauth-token",
        }, UserCaller(), ct);

        await connections.Disconnect("mail", UserCaller(), ct);

        Assert.Empty(await connections.List(ct));
        Assert.Equal("mail", (await disconnected.NextAsync(ct: ct)).ConnectionId);
    }

    [Fact]
    public async Task ProbingAnUnknownConnectionFails()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create()
            .WithModule<MyDataModule>()
            .WithModule<ComputeModule>()
            .WithModule<ConnectionsModule>()
            .StartAsync(ct);
        var connections = brain.Get<IConnections>(Owner);

        await Assert.ThrowsAsync<ConnectionNotConfiguredException>(() => connections.Probe("missing", UserCaller(), ct));
    }

    [Fact]
    public async Task AnUnknownSourceIsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create()
            .WithModule<MyDataModule>()
            .WithModule<ComputeModule>()
            .WithModule<ConnectionsModule>()
            .StartAsync(ct);
        var connections = brain.Get<IConnections>(Owner);

        await Assert.ThrowsAsync<ArgumentException>(() => connections.Connect(new ConnectConnection
        {
            Source = "nope",
            ConnectionId = "x",
            Value = "v",
        }, UserCaller(), ct));
    }

    [Fact]
    public async Task WebResearchEmitsOneSearchRequestMeterPerCall()
    {
        var ct = TestContext.Current.CancellationToken;
        var caller = AppCaller();
        var sink = new CapturingMeterSink();
        var research = new WebResearchService(new DeterministicWebResearchProvider(), sink, TimeProvider.System);

        var result = await research.SearchAsync(new WebResearchQuery("acme"), caller, ct);
        await research.BrowseAsync(new BrowseWebRequest("https://acme.test"), caller, ct);
        await research.LookupCompanyAsync(new CompanyLookup("Acme"), caller, ct);

        Assert.Single(result.Hits);
        Assert.Equal("deterministic", result.Provider);
        Assert.Equal(3, sink.Events.Count);
        Assert.All(sink.Events, meter =>
        {
            Assert.Equal(WebResearchMeters.SearchRequest, meter.MeterId);
            Assert.Equal(WebResearchMeters.RequestUnit, meter.Unit);
            Assert.Equal(1, meter.Quantity);
            Assert.Equal(caller.IntentId, meter.IntentId);
            Assert.Equal(caller.WorkspaceId, meter.WorkspaceId);
        });
    }

    [Fact]
    public void TheMcpAllowlistRegistersExactlyTheListedServers()
    {
        var services = new ServiceCollection();

        ConnectionsModule.RegisterMcpBridge(services, "salesforce=https://mcp.example.test/tools;github=https://github.example.test/mcp");

        Assert.Equal(2, services.Count(descriptor => descriptor.ServiceType == typeof(DigitalBrain.AI.Agents.IAgentToolSource)));
    }

    [Fact]
    public void AnEmptyMcpAllowlistRegistersNoServer()
    {
        var services = new ServiceCollection();

        ConnectionsModule.RegisterMcpBridge(services, null);

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(DigitalBrain.AI.Agents.IAgentToolSource));
    }

    [Fact]
    public void AMalformedMcpAllowlistEntryIsRejected()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => ConnectionsModule.RegisterMcpBridge(services, "salesforce"));
    }

    private static CallerContext UserCaller() => new()
    {
        PrincipalId = Owner,
        AccountId = Owner,
        WorkspaceId = Owner,
        Kind = CallerKind.User,
        StampedBy = TrustedEdge.AuthenticatedHttp,
    };

    private static CallerContext AppCaller() => new()
    {
        PrincipalId = "app-principal",
        AccountId = Owner,
        WorkspaceId = Owner,
        Kind = CallerKind.App,
        StampedBy = TrustedEdge.AppProxy,
        AppId = "leadgenerator",
        IntentId = "intent-1",
    };
}

internal sealed class ScriptedProbe(params ConnectionProbeOutcome[] outcomes) : IConnectionProbe
{
    private int _index;

    public Task<ConnectionProbeResult> ProbeAsync(string source, string credential, CancellationToken cancellationToken = default)
    {
        var outcome = outcomes[Math.Min(_index++, outcomes.Length - 1)];
        return Task.FromResult(new ConnectionProbeResult(outcome, "scripted"));
    }
}

internal sealed class CapturingMeterSink : IMeterSink
{
    public List<MeterEvent> Events { get; } = [];

    public ValueTask RecordAsync(MeterEvent meterEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(meterEvent);
        return ValueTask.CompletedTask;
    }
}