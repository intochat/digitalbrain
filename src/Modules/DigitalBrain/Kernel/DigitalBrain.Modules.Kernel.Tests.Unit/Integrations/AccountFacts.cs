using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Sdk.Integrations;
using DigitalBrain.Platform.Integrations;
using DigitalBrain.Sdk.Integrations.Accounts;
using DigitalBrain.Platform.Integrations.Accounts;
using DigitalBrain.Sdk.Secrets;
using DigitalBrain.Platform.Secrets;
using DigitalBrain.Testing.Unit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Core.Tests.Unit.Integrations;

public sealed class AccountFacts : IDisposable
{
    private const string Owner = "owner-1";
    private const string Canary = "canary-connection-4b8e12";

    public AccountFacts() => CallerContextStamper.Stamp(UserCaller());

    public void Dispose() => Orleans.Runtime.RequestContext.Clear();

    [Fact]
    public async Task SameNamedCredentialsInDifferentRegistriesDoNotOverwrite()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var input = new ConnectAccount { IntegrationId = "gmail", ConnectionId = "mail", Value = "first" };
        var first = await brain.Get<IIntegrationAccounts>("workspace-one").Connect(input, ct);
        var second = await brain.Get<IIntegrationAccounts>("workspace-two").Connect(input with { Value = "second" }, ct);
        Assert.NotEqual(first.Credential.Reference, second.Credential.Reference);
        var platform = UserCaller() with { Kind = CallerKind.Platform, StampedBy = TrustedEdge.Platform, AppId = "connections" };
        Assert.Equal("first", await brain.Get<ISecrets>(Owner).Resolve(platform, first.Credential, ct));
        Assert.Equal("second", await brain.Get<ISecrets>(Owner).Resolve(platform, second.Credential, ct));
    }

    [Fact]
    public async Task ConnectingStoresTheCredentialAsASecretReferenceAndProbes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var accounts = brain.Get<IIntegrationAccounts>(Owner);

        var account = await accounts.Connect(new ConnectAccount
        {
            IntegrationId = "webresearch",
            ConnectionId = "research",
            Label = "Research key",
            Value = Canary,
        }, ct);

        Assert.Equal(AccountStatus.Connected, account.Status);
        Assert.Equal("webresearch", account.IntegrationId);
        Assert.StartsWith("secret://", account.Credential.Reference, StringComparison.Ordinal);
        Assert.Equal(Owner, account.WorkspaceId);
        Assert.Single(await accounts.List(ct));
        Assert.DoesNotContain(Canary, System.Text.Json.JsonSerializer.Serialize(account), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProbeTracksExpiredThenFailingStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var scripted = new ScriptedProbe(AccountProbeOutcome.Expired, AccountProbeOutcome.Failing);
        await using var brain = await UnitTest.Create()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IAccountProbe>(scripted))
            .StartAsync(ct);
        var accounts = brain.Get<IIntegrationAccounts>(Owner);

        var connected = await accounts.Connect(new ConnectAccount
        {
            IntegrationId = "supabase",
            ConnectionId = "db",
            Value = "Host=db;Database=app;Username=reader;Password=pw",
        }, ct);
        Assert.Equal(AccountStatus.Expired, connected.Status);

        var probed = await accounts.Probe("db", ct);
        Assert.Equal(AccountStatus.Failing, probed.Status);
        Assert.NotNull(probed.LastProbedAt);
    }

    [Fact]
    public async Task DisconnectRemovesTheAccountAndPublishesASignal()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var accounts = brain.Get<IIntegrationAccounts>(Owner);
        await using var disconnected = await brain.Observe<AccountDisconnected>(accounts, ct);

        await accounts.Connect(new ConnectAccount { IntegrationId = "gmail", ConnectionId = "mail", Value = "oauth-token" }, ct);
        await accounts.Disconnect("mail", ct);

        Assert.Empty(await accounts.List(ct));
        Assert.Equal("mail", (await disconnected.NextAsync(ct: ct)).ConnectionId);
    }

    [Fact]
    public async Task ProbingAnUnknownAccountFails()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);

        await Assert.ThrowsAsync<AccountNotConfiguredException>(() => brain.Get<IIntegrationAccounts>(Owner).Probe("missing", ct));
    }

    [Fact]
    public async Task AnEmptyIntegrationIsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);

        await Assert.ThrowsAsync<ArgumentException>(() => brain.Get<IIntegrationAccounts>(Owner)
            .Connect(new ConnectAccount { IntegrationId = "", ConnectionId = "x", Value = "v" }, ct));
    }

    [Fact]
    public void TheAccountsContractIsPlatformOnly()
    {
        Assert.True(PlatformOnlyAttribute.AppliesTo(typeof(IIntegrationAccounts)));
    }

    [Fact]
    public async Task AnAppStampedAmbientCallerCannotConnectProbeOrDisconnect()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var accounts = brain.Get<IIntegrationAccounts>(Owner);
        await accounts.Connect(new ConnectAccount { IntegrationId = "gmail", ConnectionId = "mail", Value = "v" }, ct);

        CallerContextStamper.Stamp(UserCaller() with { Kind = CallerKind.App, StampedBy = TrustedEdge.AppProxy });

        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.Connect(
            new ConnectAccount { IntegrationId = "gmail", ConnectionId = "other", Value = "v" }, ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.Probe("mail", ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.Disconnect("mail", ct));
        CallerContextStamper.Stamp(UserCaller());
        Assert.Single(await accounts.List(ct));
    }

    [Theory]
    [InlineData("secret://bob/other-secret")]
    [InlineData("secret://:integrations/openai.ApiKey")]
    [InlineData("secret://")]
    public async Task ASecretReferenceIntoAnyOtherOwnersVaultIsRefusedWithoutNamingIt(string reference)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);

        var refusal = await Assert.ThrowsAsync<ArgumentException>(() => brain.Get<IIntegrationAccounts>(Owner)
            .Connect(new ConnectAccount { IntegrationId = "gmail", ConnectionId = "mail", SecretReference = reference }, ct));

        Assert.DoesNotContain(reference, refusal.Message, StringComparison.Ordinal);
        Assert.Empty(await brain.Get<IIntegrationAccounts>(Owner).List(ct));
    }

    [Fact]
    public async Task ASecretReferenceIntoTheCallersOwnVaultIsAccepted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);
        var own = await brain.Get<ISecrets>(Owner).Set(UserCaller(), "mine", "Mine", "own-value", ct);

        var account = await brain.Get<IIntegrationAccounts>(Owner)
            .Connect(new ConnectAccount { IntegrationId = "gmail", ConnectionId = "mail", SecretReference = own.Reference }, ct);

        Assert.Equal(own.Reference, account.Credential.Reference);
    }

    [Fact]
    public async Task TheCallerIsTheAmbientStampAndNothingWithoutOneIsAccepted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(ct);

        var account = await brain.Get<IIntegrationAccounts>("workspace-one")
            .Connect(new ConnectAccount { IntegrationId = "gmail", ConnectionId = "mail", Value = "mine" }, ct);

        Assert.Equal(Owner, account.Credential.Owner);
        Orleans.Runtime.RequestContext.Clear();
        await Assert.ThrowsAsync<UntrustedCallerException>(() => brain.Get<IIntegrationAccounts>("workspace-one")
            .Connect(new ConnectAccount { IntegrationId = "gmail", ConnectionId = "mail2", Value = "v" }, ct));
        CallerContextStamper.Stamp(UserCaller());
        Assert.Equal("mail", Assert.Single(await brain.Get<IIntegrationAccounts>("workspace-one").List(ct)).Id);
    }

    [Fact]
    public void CapabilitiesAreServedUnderTheBrainScopedIntegrationsRoute()
    {
        var builder = WebApplication.CreateBuilder();
        DigitalBrain.Testing.TestLogging.Apply(builder.Configuration);
        builder.Services.AddSingleton(typeof(IGrainFactory), _ => null!);
        builder.Services.AddSingleton(typeof(IDigitalBrain), _ => null!);
        IEndpointRouteBuilder app = builder.Build();
        DigitalBrain.Platform.PlatformHosting.MapDigitalBrainPlatform(app);

        var routes = app.DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText!).ToArray();

        Assert.Contains("/brains/{brainId}/integrations/capabilities", routes);
    }

    [Fact]
    public void AccountsAreServedOnlyUnderTheBrainScopedAccountsRoute()
    {
        var builder = WebApplication.CreateBuilder();
        DigitalBrain.Testing.TestLogging.Apply(builder.Configuration);
        builder.Services.AddSingleton(typeof(IGrainFactory), _ => null!);
        builder.Services.AddSingleton(typeof(IDigitalBrain), _ => null!);
        IEndpointRouteBuilder app = builder.Build();
        DigitalBrain.Platform.PlatformHosting.MapDigitalBrainPlatform(app);

        var routes = app.DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText!).ToArray();

        Assert.Contains("/brains/{brainId}/integrations/accounts/connect", routes);
        Assert.Contains("/brains/{brainId}/integrations/accounts/probe", routes);
        Assert.Contains("/brains/{brainId}/integrations/accounts/disconnect", routes);
        Assert.Contains("/brains/{brainId}/integrations/accounts/requests", routes);
        Assert.DoesNotContain(routes, route => route.StartsWith("/connections", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AccountStatus.Connected, "Configured")]
    [InlineData(AccountStatus.Expired, "Expired")]
    [InlineData(AccountStatus.Failing, "Unavailable")]
    public void StoredCredentialsDoNotImplyVerification(AccountStatus status, string expected)
        => Assert.Equal(expected, AccountEndpoints.Status(status));

    [Fact]
    public void PackageAccountsIncludeOnlyRecordsFiledUnderTheBrain()
    {
        var scope = BrainScope.Create("alice", "one");
        static IntegrationAccount Account(string id, string workspace, string owner) => new()
        {
            Id = id,
            IntegrationId = "gmail",
            WorkspaceId = workspace,
            Status = AccountStatus.Connected,
            Credential = SecretRef.For(owner, id, id, true),
        };
        var result = ScopedAccounts.Visible(scope, [Account("new", "one", "alice"), Account("wrong-workspace", "two", "alice")]);
        Assert.Equal(["new"], result.Select(account => account.Id));
    }

    private static Task<UnitBrain> StartAsync(CancellationToken cancellationToken)
        => UnitTest.Create().StartAsync(cancellationToken);

    private static CallerContext UserCaller() => new()
    {
        PrincipalId = Owner,
        AccountId = Owner,
        BrainId = Owner,
        Kind = CallerKind.User,
        StampedBy = TrustedEdge.AuthenticatedHttp,
    };
}

internal sealed class ScriptedProbe(params AccountProbeOutcome[] outcomes) : IAccountProbe
{
    private int _index;

    public Task<AccountProbeResult> ProbeAsync(string source, string credential, CancellationToken cancellationToken = default)
    {
        var outcome = outcomes[Math.Min(_index++, outcomes.Length - 1)];
        return Task.FromResult(new AccountProbeResult(outcome, "scripted"));
    }
}
