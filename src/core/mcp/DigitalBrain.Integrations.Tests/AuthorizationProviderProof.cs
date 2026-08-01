using System.Net;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using DigitalBrain.Mcp;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Testing;
using DigitalBrain.Flutter.Http;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class AuthorizationProviderProof(AuthorizationProviderProofFixture fixture)
{
    [Fact(DisplayName =
        "real HttpMcpClientSessionFactory against a fake provider parks AuthorizationRequired, follows sign-in through the real edge callback, exchanges the code, caches the token, and re-issues successfully with Bearer")]
    public async Task HappyPathParksCompletesAndReissuesWithBearer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var ui = await fixture.StartUiEdgeAsync(test, cancellationToken);
        using var browser = CreateBrowser();

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var requiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);
        ScriptSampleRead(fixture);

        var operation = test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", commandId),
                cancellationToken);

        using var race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        race.CancelAfter(TimeSpan.FromSeconds(45));
        var requiredOrFault = await Task.WhenAny(
            requiredWait,
            operation.ContinueWith(
                static task =>
                {
                    if (task.IsFaulted)
                    {
                        throw task.Exception!.GetBaseException();
                    }

                    throw new InvalidOperationException("Gmail completed before AuthorizationRequired.");
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default));
        await requiredOrFault.WaitAsync(race.Token);
        var required = (await requiredWait).Synapse;
        Assert.Equal(commandId, required.CommandId);
        Assert.Equal(IntegrationsFixture.GmailServerKey, required.ServerKey);
        Assert.Equal("DigitalBrain Gmail", required.ServerDisplayName);
        Assert.False(string.IsNullOrWhiteSpace(required.State));
        Assert.StartsWith(
            fixture.Provider.AuthorizeEndpoint.GetLeftPart(UriPartial.Path),
            required.SignInUrl.GetLeftPart(UriPartial.Path),
            StringComparison.Ordinal);
        Assert.Contains(required.State, required.SignInUrl.AbsoluteUri, StringComparison.Ordinal);
        await AssertJournalHasNoSecretsAsync(auth, required.State, cancellationToken);

        var completedWait = auth.Outgoing.NextAsync<AuthorizationCompleted>(cancellationToken);
        var completed = (await completedWait).Synapse;
        Assert.Equal(commandId, completed.CommandId);
        Assert.Equal(required.State, completed.State);

        var response = await operation.WaitAsync(cancellationToken);
        Assert.Equal(
            new GmailMessage(
                IntegrationsFixture.SampleMessageId,
                IntegrationsFixture.SampleSubject,
                IntegrationsFixture.SampleSender,
                IntegrationsFixture.SampleBody),
            Assert.Single(response.Messages));
        Assert.True(fixture.Provider.BearerHits >= 1);
        Assert.StartsWith(
            FakeMcpProviderHost.AccessTokenPrefix,
            fixture.Provider.LastBearerToken,
            StringComparison.Ordinal);

        ScriptSampleRead(fixture);
        var reissued = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", commandId),
                cancellationToken);
        Assert.Equal(IntegrationsFixture.SampleMessageId, Assert.Single(reissued.Messages).Id);
        Assert.Single(await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken));
        Assert.Single(await auth.Outgoing.ReadAsync<AuthorizationCompleted>(afterSequence: 0, cancellationToken));
        await AssertJournalHasNoSecretsAsync(auth, required.State, cancellationToken);
    }

    [Fact(DisplayName =
        "fake provider access_denied through the real edge journals AuthorizationDenied and re-issue fails typed without looping")]
    public async Task DeniedThroughEdgeFailsTypedOnReissue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var ui = await fixture.StartUiEdgeAsync(test, cancellationToken);

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var requiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);

        fixture.Provider.DenyNextAuthorization = true;
        using var hang = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var operation = test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", commandId),
                hang.Token);

        var required = (await requiredWait).Synapse;
        var deniedWait = auth.Outgoing.NextAsync<AuthorizationDenied>(cancellationToken);
        var denied = (await deniedWait).Synapse;
        Assert.Equal(commandId, denied.CommandId);
        Assert.Equal(required.State, denied.State);

        await hang.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);

        var reissued = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", commandId),
                cancellationToken);
        Assert.False(reissued.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(reissued.Error));
        Assert.Single(await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken));
        Assert.Empty(await auth.Outgoing.ReadAsync<AuthorizationCompleted>(afterSequence: 0, cancellationToken));
    }

    [Fact(DisplayName =
        "state mismatch at the real edge callback is rejected 400 and nothing journals completed")]
    public async Task StateMismatchAtEdgeIsRejectedWithoutCompletion()
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
                IntegrationsFixture.GmailServerKey,
                "DigitalBrain Gmail",
                new Uri(fixture.Provider.AuthorizeEndpoint, "?state=pending-state"),
                "pending-state"),
            cancellationToken);

        using var mismatch = await browser.GetAsync(
            new Uri(fixture.EdgeCallbackAddress, "?state=foreign-state&code=nope"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);

        var completed = await auth.Outgoing.ReadAsync<AuthorizationCompleted>(afterSequence: 0, cancellationToken);
        Assert.Empty(completed);
    }

    [Fact(DisplayName =
        "short-lived token from the fake provider expires and the next operation parks again via preflight")]
    public async Task ExpiredTokenParksAgainViaPreflight()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        await using var ui = await fixture.StartUiEdgeAsync(test, cancellationToken);

        var seedCommand = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var seedRequiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);

        ScriptSampleRead(fixture);
        var seedOperation = test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", seedCommand),
                cancellationToken);
        var seedRequired = (await seedRequiredWait).Synapse;
        _ = await seedOperation.WaitAsync(cancellationToken);
        await test.Clock.AdvanceAsync(TimeSpan.FromHours(2), cancellationToken);

        var expiredCommand = CommandId.New();
        var expiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);
        using var hang = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        hang.CancelAfter(TimeSpan.FromSeconds(30));
        var expiredSend = test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", expiredCommand),
                hang.Token);

        var expired = (await expiredWait).Synapse;
        Assert.Equal(expiredCommand, expired.CommandId);
        Assert.NotEqual(seedRequired.State, expired.State);
        var requiredFacts = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        Assert.Equal(2, requiredFacts.Count);

        await hang.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => expiredSend);
    }

    private static void ScriptSampleRead(AuthorizationProviderProofFixture fixture)
    {
        fixture.PlannerChat.ReplyWithCapabilityCall(
            IntegrationsFixture.GmailGetMessageTool,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["messageId"] = IntegrationsFixture.SampleMessageId,
                ["messageFormat"] = "FULL_CONTENT",
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
            Assert.DoesNotContain("refresh_token", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Bearer", payload, StringComparison.Ordinal);
            Assert.Contains(state, payload, StringComparison.Ordinal);
        }
    }
}
