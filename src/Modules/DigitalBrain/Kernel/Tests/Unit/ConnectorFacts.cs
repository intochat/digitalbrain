using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Sdk.Connectors;
using DigitalBrain.Sdk.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Runtime.Tests.Unit;

public sealed class ConnectFlowFacts
{
    private const string Owner = "owner-1";
    private const string Canary = "canary-connection-4b8e12";

    [Fact]
    public async Task ConnectingStoresTheCredentialAsASecretReferenceAndProbes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create()
            .WithModule<SecretsModule>()
            .WithModule<ConnectorModule>()
            .StartAsync(ct);
        var connections = brain.Get<IConnectors>(Owner);

        var record = await connections.Connect(new ConnectRequest
        {
            Source = "webresearch",
            ConnectionId = "research",
            Label = "Research key",
            Value = Canary,
        }, UserCaller(), ct);

        Assert.Equal(ConnectorStatus.Connected, record.Status);
        Assert.Equal("webresearch", record.Source);
        Assert.StartsWith("secret://", record.Credential.Reference, StringComparison.Ordinal);
        Assert.Equal(Owner, record.WorkspaceId);

        var list = await connections.List(ct);
        Assert.Single(list);

        Assert.DoesNotContain(Canary, System.Text.Json.JsonSerializer.Serialize(record), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProbeTracksExpiredThenFailingStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var scripted = new ScriptedProbe(ConnectorProbeOutcome.Expired, ConnectorProbeOutcome.Failing);
        await using var brain = await UnitTest.Create()
            .WithModule<SecretsModule>()
            .WithModule<ConnectorModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IConnectorProbe>(scripted))
            .StartAsync(ct);
        var connections = brain.Get<IConnectors>(Owner);

        var connected = await connections.Connect(new ConnectRequest
        {
            Source = "supabase",
            ConnectionId = "db",
            Value = "Host=db;Database=app;Username=reader;Password=pw",
        }, UserCaller(), ct);
        Assert.Equal(ConnectorStatus.Expired, connected.Status);

        var probed = await connections.Probe("db", UserCaller(), ct);
        Assert.Equal(ConnectorStatus.Failing, probed.Status);
        Assert.NotNull(probed.LastProbedAt);
    }

    [Fact]
    public async Task DisconnectRemovesTheConnectionAndPublishesASignal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create()
            .WithModule<SecretsModule>()
            .WithModule<ConnectorModule>()
            .StartAsync(ct);
        var connections = brain.Get<IConnectors>(Owner);
        await using var disconnected = await brain.Observe<ConnectorDisconnected>(connections, ct);

        await connections.Connect(new ConnectRequest
        {
            Source = "gmail",
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
            .WithModule<SecretsModule>()
            .WithModule<ConnectorModule>()
            .StartAsync(ct);
        var connections = brain.Get<IConnectors>(Owner);

        await Assert.ThrowsAsync<ConnectorNotConfiguredException>(() => connections.Probe("missing", UserCaller(), ct));
    }

    [Fact]
    public async Task AnEmptySourceIsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create()
            .WithModule<SecretsModule>()
            .WithModule<ConnectorModule>()
            .StartAsync(ct);
        var connections = brain.Get<IConnectors>(Owner);

        await Assert.ThrowsAsync<ArgumentException>(() => connections.Connect(new ConnectRequest
        {
            Source = "",
            ConnectionId = "x",
            Value = "v",
        }, UserCaller(), ct));
    }

    private static CallerContext UserCaller() => new()
    {
        PrincipalId = Owner,
        AccountId = Owner,
        WorkspaceId = Owner,
        Kind = CallerKind.User,
        StampedBy = TrustedEdge.AuthenticatedHttp,
    };
}

internal sealed class ScriptedProbe(params ConnectorProbeOutcome[] outcomes) : IConnectorProbe
{
    private int _index;

    public Task<ConnectorProbeResult> ProbeAsync(string source, string credential, CancellationToken cancellationToken = default)
    {
        var outcome = outcomes[Math.Min(_index++, outcomes.Length - 1)];
        return Task.FromResult(new ConnectorProbeResult(outcome, "scripted"));
    }
}
