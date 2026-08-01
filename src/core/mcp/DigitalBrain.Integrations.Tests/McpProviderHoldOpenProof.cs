using System.Net;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Mcp;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Salesforce;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

// Assembly fixture + method lease + ResetForTests: one cluster for the suite, hard abort of any
// parked hold-open between methods so dispose at suite end is not pinned.
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
        var operation = test.Client.Get<ISalesforce>(IntegrationsFixture.SalesforceServerKey)
            .SendAsync(
                new SalesforceRequest($"Query Account {IntegrationsFixture.SampleAccountId}", commandId),
                cancellationToken);

        var required = (await requiredWait).Synapse;
        using var consent = await browser.GetAsync(required.SignInUrl, cancellationToken);
        Assert.True(consent.IsSuccessStatusCode, await consent.Content.ReadAsStringAsync(cancellationToken));

        // Open must settle after deny — terminal abort abandons CreateAsync without awaiting it.
        using var settleHang = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var settled = await Task.WhenAny(operation, Task.Delay(Timeout.InfiniteTimeSpan, settleHang.Token));
        Assert.True(
            ReferenceEquals(settled, operation),
            $"Denied hold-open open did not complete within 20s — status={operation.Status}.");
        if (operation.IsCompletedSuccessfully)
        {
            var failed = await operation;
            Assert.False(failed.Succeeded);
            Assert.False(string.IsNullOrWhiteSpace(failed.Error));
        }
        else
        {
            Assert.True(operation.IsFaulted || operation.IsCanceled, $"status={operation.Status}");
        }

        var denied = await auth.Outgoing.ReadAsync<AuthorizationDenied>(afterSequence: 0, cancellationToken);
        Assert.Contains(denied, entry => entry.Synapse.CommandId == commandId && entry.Synapse.State == required.State);
        Assert.Single(await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken));
        Assert.Empty(await auth.Outgoing.ReadAsync<AuthorizationCompleted>(afterSequence: 0, cancellationToken));

        // Same command id: Claim is durable Denied, so the rail fails typed without a second park.
        using var reissueHang = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var reissuedTask = test.Client.Get<ISalesforce>(IntegrationsFixture.SalesforceServerKey)
            .SendAsync(
                new SalesforceRequest($"Query Account {IntegrationsFixture.SampleAccountId}", commandId),
                reissueHang.Token);
        var reissueSettled = await Task.WhenAny(
            reissuedTask,
            Task.Delay(Timeout.InfiniteTimeSpan, reissueHang.Token));
        Assert.True(
            ReferenceEquals(reissueSettled, reissuedTask),
            $"Denied re-issue parked a second hold-open — status={reissuedTask.Status}.");
        var reissued = await reissuedTask;
        Assert.False(reissued.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(reissued.Error));
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

    [Fact(DisplayName =
        "SF MCP hold-open: foreign-state callback during a live park does not complete the session, and cancel releases CreateAsync so the next method can run")]
    public async Task ForeignStateDuringHoldOpenDoesNotCompleteAndCancelReleasesOpen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var ui = await fixture.StartUiEdgeAsync(test, cancellationToken);
        using var browser = CreateBrowser();

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var requiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);

        using var operationLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var operation = test.Client.Get<ISalesforce>(IntegrationsFixture.SalesforceServerKey)
            .SendAsync(
                new SalesforceRequest($"Query Account {IntegrationsFixture.SampleAccountId}", commandId),
                operationLifetime.Token);
        var required = (await requiredWait).Synapse;
        Assert.False(string.IsNullOrWhiteSpace(required.State));

        using var foreign = await browser.GetAsync(
            new Uri(fixture.AppCallbackAddress, "?state=foreign-state&code=nope"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Empty(await auth.Outgoing.ReadAsync<AuthorizationCompleted>(afterSequence: 0, cancellationToken));
        Assert.Empty(await auth.Outgoing.ReadAsync<AuthorizationDenied>(afterSequence: 0, cancellationToken));
        Assert.Equal(TaskStatus.WaitingForActivation, operation.Status);

        // Foreign state must not complete the live park. Cancel (product teardown path) must.
        await operationLifetime.CancelAsync();
        McpAuthorizationCodeHub.AbortOpen(commandId);
        using var settleHang = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var settled = await Task.WhenAny(operation, Task.Delay(Timeout.InfiniteTimeSpan, settleHang.Token));
        Assert.True(
            ReferenceEquals(settled, operation),
            $"Cancel/AbortOpen did not release hold-open within 30s — status={operation.Status}.");
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
