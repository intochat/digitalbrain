using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using DigitalBrain.Mcp;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

[Collection(GmailFakeHostTestGroup.Name)]
public sealed class AuthorizationProviderProof(AuthorizationProviderProofFixture fixture)
{
    [Fact(DisplayName =
        "Gmail SDK auth rail parks AuthorizationRequired, completes via DeliverCallback, exchanges the code, and re-issues successfully")]
    public async Task HappyPathParksCompletesAndReissuesWithBearer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var requiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);
        ScriptSampleRead(fixture);

        using var hang = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var parked = test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", commandId),
                hang.Token);

        var required = (await requiredWait).Synapse;
        Assert.Equal(commandId, required.CommandId);
        Assert.Equal(IntegrationsFixture.GmailServerKey, required.ServerKey);
        Assert.Equal("DigitalBrain Gmail", required.ServerDisplayName);
        Assert.False(string.IsNullOrWhiteSpace(required.State));
        Assert.Contains("accounts.google.com", required.SignInUrl.Host, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(required.State, required.SignInUrl.AbsoluteUri, StringComparison.Ordinal);
        await AssertJournalHasNoSecretsAsync(auth, required.State, cancellationToken);

        var completedWait = auth.Outgoing.NextAsync<AuthorizationCompleted>(cancellationToken);
        _ = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(required.State, "provider-proof-code", Error: null, Iss: null),
            cancellationToken);
        var completed = (await completedWait).Synapse;
        Assert.Equal(commandId, completed.CommandId);
        Assert.Equal(required.State, completed.State);

        await hang.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parked);

        ScriptSampleRead(fixture);
        var response = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", commandId),
                cancellationToken);
        Assert.Equal(
            new GmailMessage(
                IntegrationsFixture.SampleMessageId,
                IntegrationsFixture.SampleSubject,
                IntegrationsFixture.SampleSender,
                IntegrationsFixture.SampleBody),
            Assert.Single(response.Messages));

        ScriptSampleRead(fixture);
        var reissued = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", CommandId.New()),
                cancellationToken);
        Assert.Equal(IntegrationsFixture.SampleMessageId, Assert.Single(reissued.Messages).Id);
        Assert.Single(await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken));
        Assert.Single(await auth.Outgoing.ReadAsync<AuthorizationCompleted>(afterSequence: 0, cancellationToken));
        await AssertJournalHasNoSecretsAsync(auth, required.State, cancellationToken);
    }

    [Fact(DisplayName =
        "access_denied journals AuthorizationDenied and re-issue fails typed without looping")]
    public async Task DeniedThroughEdgeFailsTypedOnReissue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var requiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);

        using var hang = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var operation = test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", commandId),
                hang.Token);

        var required = (await requiredWait).Synapse;
        var deniedWait = auth.Outgoing.NextAsync<AuthorizationDenied>(cancellationToken);
        _ = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(required.State, Code: null, Error: "access_denied", Iss: null),
            cancellationToken);
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
        "state mismatch at DeliverCallback is rejected and nothing journals completed")]
    public async Task StateMismatchAtEdgeIsRejectedWithoutCompletion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        await auth.Reference.Begin(
            new BeginMcpAuthorization(
                commandId,
                IntegrationsFixture.GmailServerKey,
                "DigitalBrain Gmail",
                new Uri("https://accounts.google.com/o/oauth2/v2/auth?state=pending-state"),
                "pending-state"),
            cancellationToken);

        var delivery = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback("foreign-state", Code: "nope", Error: null, Iss: null),
            cancellationToken);
        Assert.False(delivery.Accepted);

        var completed = await auth.Outgoing.ReadAsync<AuthorizationCompleted>(afterSequence: 0, cancellationToken);
        Assert.Empty(completed);
    }

    [Fact(DisplayName =
        "short-lived token expires and the next operation parks again via permanent refresh failure")]
    public async Task ExpiredTokenParksAgainViaPreflight()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        var seedCommand = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var seedRequiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);

        using var seedHang = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var seedOperation = test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", seedCommand),
                seedHang.Token);
        var seedRequired = (await seedRequiredWait).Synapse;
        _ = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(seedRequired.State, "seed-code", Error: null, Iss: null),
            cancellationToken);
        await seedHang.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => seedOperation);

        ScriptSampleRead(fixture);
        _ = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", seedCommand),
                cancellationToken);

        // Reset in `finally`: TokenHost is shared with every other class in
        // GmailFakeHostTestGroup, and a poisoned RefreshStatusCode would leak past this test.
        IntegrationsGmailHosts.TokenHost.RefreshStatusCode = System.Net.HttpStatusCode.BadRequest;
        IntegrationsGmailHosts.TokenHost.RefreshError = new { error = "invalid_grant" };
        try
        {
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
        finally
        {
            IntegrationsGmailHosts.TokenHost.RefreshStatusCode = System.Net.HttpStatusCode.OK;
            IntegrationsGmailHosts.TokenHost.RefreshError = null;
        }
    }

    private static void ScriptSampleRead(AuthorizationProviderProofFixture fixture)
    {
        fixture.PlannerChat.ReplyWithCapabilityCall(
            IntegrationsFixture.GmailGetMessageTool,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["id"] = IntegrationsFixture.SampleMessageId,
                ["format"] = "FULL",
            });
        fixture.PlannerChat.Reply("done");
    }

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
