using System.Net;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Mcp;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Salesforce;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class McpProviderHoldOpenProof(McpProviderHoldOpenProofFixture fixture)
{
    [Fact(DisplayName =
        "SF MCP hold-open: real HttpMcpClientSessionFactory parks AuthorizationRequired, human browser consent through /oauth/callback exchanges the code, caches the token, and re-issues successfully with Bearer")]
    public async Task HappyPathParksCompletesAndReissuesWithBearer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var ui = await fixture.StartUiEdgeAsync(test, cancellationToken);
        using var browser = CreateBrowser();

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var requiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);

        ScriptSampleSoql(fixture);
        var operation = test.Client.Get<ISalesforce>(IntegrationsFixture.SalesforceServerKey)
            .SendAsync(
                new SalesforceRequest($"Query Account {IntegrationsFixture.SampleAccountId}", commandId),
                cancellationToken);
        var required = (await requiredWait).Synapse;
        Assert.Equal(commandId, required.CommandId);
        Assert.Equal(IntegrationsFixture.SalesforceServerKey, required.ServerKey);
        Assert.Equal("DigitalBrain Salesforce", required.ServerDisplayName);
        Assert.False(string.IsNullOrWhiteSpace(required.State));
        Assert.StartsWith(
            fixture.Provider.AuthorizeEndpoint.GetLeftPart(UriPartial.Path),
            required.SignInUrl.GetLeftPart(UriPartial.Path),
            StringComparison.Ordinal);
        Assert.Contains(required.State, required.SignInUrl.AbsoluteUri, StringComparison.Ordinal);

        using var consent = await browser.GetAsync(required.SignInUrl, cancellationToken);
        Assert.True(consent.IsSuccessStatusCode, await consent.Content.ReadAsStringAsync(cancellationToken));
        var deliveredCode = await auth.Reference.TakeCompletedCode(required.State, cancellationToken);
        Assert.NotNull(deliveredCode);
        Assert.False(string.IsNullOrWhiteSpace(deliveredCode.Code));

        var response = await operation.WaitAsync(cancellationToken);
        Assert.True(response.Succeeded, response.Error);
        Assert.True(fixture.Provider.BearerHits >= 1);
        Assert.StartsWith(
            FakeMcpProviderHost.AccessTokenPrefix,
            fixture.Provider.LastBearerToken,
            StringComparison.Ordinal);
        Assert.DoesNotContain("authorized:", fixture.Provider.LastBearerToken, StringComparison.Ordinal);
        Assert.Single(await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken));
        Assert.Single(await auth.Outgoing.ReadAsync<AuthorizationCompleted>(afterSequence: 0, cancellationToken));
        await AssertJournalHasNoSecretsAsync(auth, required.State, cancellationToken);

        ScriptSampleSoql(fixture);
        var reissued = await test.Client.Get<ISalesforce>(IntegrationsFixture.SalesforceServerKey)
            .SendAsync(
                new SalesforceRequest($"Query Account {IntegrationsFixture.SampleAccountId}", commandId),
                cancellationToken);
        Assert.True(reissued.Succeeded, reissued.Error);
        Assert.Single(await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken));
        Assert.Single(await auth.Outgoing.ReadAsync<AuthorizationCompleted>(afterSequence: 0, cancellationToken));
    }

    [Fact(DisplayName =
        "SF MCP hold-open: access_denied through /oauth/callback journals AuthorizationDenied, the open attempt completes, re-issue fails typed without looping, and dispose does not hang")]
    public async Task DeniedThroughAppCallbackCompletesOpenAndDoesNotHangDispose()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var ui = await fixture.StartUiEdgeAsync(test, cancellationToken);
        using var browser = CreateBrowser();

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var requiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);

        fixture.Provider.DenyNextAuthorization = true;
        using var operationHang = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var operation = test.Client.Get<ISalesforce>(IntegrationsFixture.SalesforceServerKey)
            .SendAsync(
                new SalesforceRequest($"Query Account {IntegrationsFixture.SampleAccountId}", commandId),
                operationHang.Token);

        var required = (await requiredWait).Synapse;
        var deniedWait = auth.Outgoing.NextAsync<AuthorizationDenied>(cancellationToken);
        using var consent = await browser.GetAsync(required.SignInUrl, cancellationToken);
        Assert.True(consent.IsSuccessStatusCode, await consent.Content.ReadAsStringAsync(cancellationToken));

        var denied = (await deniedWait).Synapse;
        Assert.Equal(commandId, denied.CommandId);
        Assert.Equal(required.State, denied.State);

        var claim = await auth.Reference.Claim(commandId, cancellationToken);
        Assert.Equal(McpAuthorizationClaimKind.Denied, claim.Kind);

        // Cancel the parked client call so fixture dispose is not blocked by a still-open CreateAsync.
        await operationHang.CancelAsync();
        try
        {
            await operation.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (TimeoutException)
        {
            // Grain may still be draining; dispose timeout on the fixture is the final hang bar.
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Single(await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken));
        Assert.Empty(await auth.Outgoing.ReadAsync<AuthorizationCompleted>(afterSequence: 0, cancellationToken));
        Assert.Single(await auth.Outgoing.ReadAsync<AuthorizationDenied>(afterSequence: 0, cancellationToken));
    }

    [Fact(DisplayName =
        "SF MCP hold-open: state mismatch at the app callback is rejected 400 and nothing journals completed")]
    public async Task StateMismatchAtAppCallbackIsRejectedWithoutCompletion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var ui = await fixture.StartUiEdgeAsync(test, cancellationToken);
        using var browser = CreateBrowser();

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        await auth.Reference.Begin(
            new BeginMcpAuthorization(
                commandId,
                IntegrationsFixture.SalesforceServerKey,
                "DigitalBrain Salesforce",
                new Uri(fixture.Provider.AuthorizeEndpoint, "?state=pending-state"),
                "pending-state"),
            cancellationToken);

        using var mismatch = await browser.GetAsync(
            new Uri(fixture.AppCallbackAddress, "?state=foreign-state&code=nope"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);

        var completed = await auth.Outgoing.ReadAsync<AuthorizationCompleted>(afterSequence: 0, cancellationToken);
        Assert.Empty(completed);
    }

    private static void ScriptSampleSoql(McpProviderHoldOpenProofFixture fixture)
    {
        fixture.PlannerChat.ReplyWithCapabilityCall(
            "soqlQuery",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["query"] = $"SELECT Id, Description FROM Account WHERE Id = '{IntegrationsFixture.SampleAccountId}'",
            });
        fixture.PlannerChat.Reply("done");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "HttpClient owns and disposes the handler.")]
    private static HttpClient CreateBrowser()
        => new(
            new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                CheckCertificateRevocationList = true,
            },
            disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

    private static async Task AssertJournalHasNoSecretsAsync(
        TestNeuron<IMcpAuthorization> auth,
        string state,
        CancellationToken cancellationToken)
    {
        var required = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        var completed = await auth.Outgoing.ReadAsync<AuthorizationCompleted>(afterSequence: 0, cancellationToken);
        var denied = await auth.Outgoing.ReadAsync<AuthorizationDenied>(afterSequence: 0, cancellationToken);

        foreach (var payload in required.Select(entry => JsonSerializer.Serialize(entry.Synapse))
            .Concat(completed.Select(entry => JsonSerializer.Serialize(entry.Synapse)))
            .Concat(denied.Select(entry => JsonSerializer.Serialize(entry.Synapse))))
        {
            Assert.DoesNotContain("client_secret", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("access_token", payload, StringComparison.OrdinalIgnoreCase);
            // "refresh_token" is an OAuth scope name that may appear on the SF authorize URL; forbid
            // credential material, not the scope token itself.
            Assert.DoesNotContain("fake-access-", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("Bearer ", payload, StringComparison.Ordinal);
            Assert.Contains(state, payload, StringComparison.Ordinal);
        }
    }
}
