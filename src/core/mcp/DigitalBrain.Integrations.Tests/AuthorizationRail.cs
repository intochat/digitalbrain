using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using DigitalBrain.Mcp;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class AuthorizationRail(AuthorizationRailFixture fixture)
{
    [Fact(DisplayName =
        "no token journals AuthorizationRequired with sign-in URL and state, and keeps secrets out of the journal")]
    public async Task MissingTokenParksAsAuthorizationRequiredWithoutSecrets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        GmailHelpers.CatalogSampleMessage(test);

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var requiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);

        using var hang = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        hang.CancelAfter(TimeSpan.FromSeconds(15));
        var send = test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", commandId),
                hang.Token);

        var required = (await requiredWait).Synapse;
        Assert.Equal(commandId, required.CommandId);
        Assert.Equal(IntegrationsFixture.GmailServerKey, required.ServerKey);
        Assert.Equal("DigitalBrain Gmail", required.ServerDisplayName);
        Assert.False(string.IsNullOrWhiteSpace(required.State));
        Assert.Contains(required.State, required.SignInUrl.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("accounts.google.com", required.SignInUrl.Host, StringComparison.OrdinalIgnoreCase);
        await AssertJournalHasNoSecretsAsync(auth, required.State, cancellationToken);

        await hang.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
    }

    [Fact(DisplayName =
        "expired refresh uses the same authorization-required rail")]
    public async Task ExpiredTokenParksAsAuthorizationRequired()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        GmailHelpers.CatalogSampleMessage(test);

        var seedCommand = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);

        var seedRequiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);
        using var seedHang = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var seedSend = test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", seedCommand),
                seedHang.Token);
        var seedRequired = (await seedRequiredWait).Synapse;
        Assert.NotNull(seedRequired);

        var seedCompleted = auth.Outgoing.NextAsync<AuthorizationCompleted>(cancellationToken);
        await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(seedRequired.State, "seed-code", Error: null, Iss: null),
            cancellationToken);
        _ = await seedCompleted;

        await seedHang.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => seedSend);

        GmailHelpers.ScriptReadSampleMessage(test);
        var seeded = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", seedCommand),
                cancellationToken);
        Assert.Equal(IntegrationsFixture.SampleMessageId, Assert.Single(seeded.Messages).Id);

        // Permanent refresh failure clears custody and re-parks once the access token is
        // considered expired under the silo's TimeProvider-backed Google IClock.
        IntegrationsGmailHosts.TokenHost.RefreshStatusCode = System.Net.HttpStatusCode.BadRequest;
        IntegrationsGmailHosts.TokenHost.RefreshError = new { error = "invalid_grant", error_description = "expired" };
        await test.Clock.AdvanceAsync(TimeSpan.FromHours(2), cancellationToken);

        var expiredCommand = CommandId.New();
        var expiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);
        using var expiredHang = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        expiredHang.CancelAfter(TimeSpan.FromSeconds(15));
        var expiredSend = test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", expiredCommand),
                expiredHang.Token);

        var expired = (await expiredWait).Synapse;
        Assert.Equal(expiredCommand, expired.CommandId);
        Assert.NotEqual(seedRequired.State, expired.State);
        var requiredFacts = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        Assert.Equal(2, requiredFacts.Count);
        Assert.Contains(requiredFacts, observed => observed.Synapse == expired);

        await expiredHang.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => expiredSend);
    }

    [Fact(DisplayName =
        "denied authorization journals AuthorizationDenied and re-issued command fails typed without looping")]
    public async Task DeniedAuthorizationFailsTypedOnReissueWithoutLoop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        GmailHelpers.CatalogSampleMessage(test);

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);

        var requiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);
        using var hang = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var first = test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", commandId),
                hang.Token);
        var required = (await requiredWait).Synapse;
        Assert.NotNull(required);

        await hang.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        var deniedWait = auth.Outgoing.NextAsync<AuthorizationDenied>(cancellationToken);
        var delivery = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(
                required.State,
                Code: null,
                Error: "access_denied",
                Iss: null),
            cancellationToken);

        Assert.True(delivery.Accepted);
        Assert.True(delivery.Denied);
        Assert.False(delivery.Completed);

        var denied = (await deniedWait).Synapse;
        Assert.Equal(commandId, denied.CommandId);
        Assert.Equal(required.State, denied.State);

        var reissued = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", commandId),
                cancellationToken);

        Assert.False(reissued.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(reissued.Error));
        var requiredAgain = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        Assert.Single(requiredAgain);
    }

    [Fact(DisplayName =
        "completed authorization delivers code through the edge seam, caches the token, journals AuthorizationCompleted, and re-issues the original CommandId")]
    public async Task CompletedAuthorizationResumesOriginalCommand()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        GmailHelpers.CatalogSampleMessage(test);

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var requiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);

        using var hang = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var parked = test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", commandId),
                hang.Token);

        var required = (await requiredWait).Synapse;
        Assert.NotNull(required);

        var completedWait = auth.Outgoing.NextAsync<AuthorizationCompleted>(cancellationToken);
        var delivery = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(
                required.State,
                Code: "edge-delivered-code",
                Error: null,
                Iss: "https://accounts.google.com"),
            cancellationToken);

        Assert.True(delivery.Accepted);
        Assert.True(delivery.Completed);
        Assert.False(delivery.Denied);

        var completed = (await completedWait).Synapse;
        Assert.Equal(commandId, completed.CommandId);
        Assert.Equal(required.State, completed.State);

        await hang.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parked);

        GmailHelpers.ScriptReadSampleMessage(test);
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
        await AssertJournalHasNoSecretsAsync(auth, required.State, cancellationToken);
        Assert.Single(await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken));
        Assert.Single(await auth.Outgoing.ReadAsync<AuthorizationCompleted>(afterSequence: 0, cancellationToken));

        var taken = await auth.Reference.TakeCompletedCode(required.State, cancellationToken);
        Assert.Equal("edge-delivered-code", taken?.Code);
        Assert.Equal("https://accounts.google.com", taken?.Iss);

        // After a real code exchange the durable refresh token authorizes a new command without
        // re-parking. (A Completed claim alone must not fabricate tokens — that is covered by the
        // re-issue path using TakeCompletedCode + provider exchange, not a marker string.)
        GmailHelpers.ScriptReadSampleMessage(test);
        var nextResponse = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", CommandId.New()),
                cancellationToken);
        Assert.Equal(
            new GmailMessage(
                IntegrationsFixture.SampleMessageId,
                IntegrationsFixture.SampleSubject,
                IntegrationsFixture.SampleSender,
                IntegrationsFixture.SampleBody),
            Assert.Single(nextResponse.Messages));
        Assert.Single(await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken));
    }

    [Fact(DisplayName =
        "pending authorization code is protected at rest — durable payload bytes do not contain the raw code")]
    public async Task PendingAuthorizationCodeIsProtectedAtRest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var commandId = CommandId.New();
        var state = Guid.NewGuid().ToString("N");
        var rawCode = $"raw-oauth-code-{Guid.NewGuid():N}-must-not-be-plaintext";

        await auth.Reference.Begin(
            new BeginMcpAuthorization(
                commandId,
                IntegrationsFixture.GmailServerKey,
                "DigitalBrain Gmail",
                new Uri($"{AuthorizationRailFixture.PublicSignInBase}oauth/authorize?state={state}"),
                state),
            cancellationToken);

        var delivery = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(state, rawCode, Error: null, Iss: null),
            cancellationToken);
        Assert.True(delivery.Completed);

        var taken = await auth.Reference.TakeCompletedCode(state, cancellationToken);
        Assert.Equal(rawCode, taken?.Code);

        Assert.True(
            AuthorizationCodeCustodyProbe.TryGetDurablePayload(state, out var durableBytes),
            "Expected durable pending payload to be recorded at write time.");
        Assert.NotEmpty(durableBytes);
        Assert.False(
            ContainsAscii(durableBytes, rawCode),
            "Durable pending authorization payload must not embed the raw OAuth code as UTF-8.");
        Assert.True(
            AuthorizationCodeCustodyProbe.TryGetProtectedCode(state, out var protectedCode),
            "Expected a protected code envelope.");
        Assert.False(
            ContainsAscii(protectedCode, rawCode),
            "Protected code envelope must not embed the raw OAuth code as UTF-8.");
    }

    [Fact(DisplayName =
        "state mismatch at the callback is rejected and nothing is journaled as completed")]
    public async Task StateMismatchIsRejectedWithoutCompletion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        GmailHelpers.CatalogSampleMessage(test);

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var requiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);

        using var hang = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        hang.CancelAfter(TimeSpan.FromSeconds(15));
        var send = test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", commandId),
                hang.Token);
        _ = await requiredWait;

        var delivery = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(
                "foreign-state",
                Code: "nope",
                Error: null,
                Iss: null),
            cancellationToken);

        Assert.False(delivery.Accepted);
        Assert.False(delivery.Completed);
        Assert.False(delivery.Denied);

        var completed = await auth.Outgoing.ReadAsync<AuthorizationCompleted>(afterSequence: 0, cancellationToken);
        Assert.Empty(completed);

        await hang.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
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
            Assert.DoesNotContain("edge-delivered-code", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("seed-code", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("client_secret", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("access_token", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("refresh_token", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Bearer", payload, StringComparison.Ordinal);
            Assert.Contains(state, payload, StringComparison.Ordinal);
        }
    }

    private static bool ContainsAscii(byte[] haystack, string needle)
    {
        var needleBytes = System.Text.Encoding.UTF8.GetBytes(needle);
        if (needleBytes.Length == 0 || haystack.Length < needleBytes.Length)
        {
            return false;
        }

        for (var i = 0; i <= haystack.Length - needleBytes.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < needleBytes.Length; j++)
            {
                if (haystack[i + j] != needleBytes[j])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }
}

