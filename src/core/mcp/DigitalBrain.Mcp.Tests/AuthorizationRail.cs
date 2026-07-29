using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using DigitalBrain.Mcp;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class AuthorizationRail(AuthorizationRailFixture fixture)
{
    [Fact(DisplayName =
        "no token journals AuthorizationRequired with sign-in URL and state, completes as authorization-required, and keeps secrets out of the journal")]
    public async Task MissingTokenParksAsAuthorizationRequiredWithoutSecrets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(test);

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var requiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);
        var driver = test.Neuron<IIntegrationDriver>("auth-missing");

        var failure = await Assert.ThrowsAsync<McpAuthorizationRequiredException>(() =>
            driver.Reference.ReadGmailMessage(
                commandId,
                IntegrationsFixture.SampleGmailAccount,
                IntegrationsFixture.SampleMessageId,
                cancellationToken));

        var required = (await requiredWait).Synapse;
        Assert.Equal(commandId, required.CommandId);
        Assert.Equal(IntegrationsFixture.GmailServerKey, required.ServerKey);
        Assert.Equal("DigitalBrain Gmail", required.ServerDisplayName);
        Assert.False(string.IsNullOrWhiteSpace(required.State));
        Assert.Contains(required.State, required.SignInUrl.AbsoluteUri, StringComparison.Ordinal);
        Assert.StartsWith(
            AuthorizationRailFixture.PublicSignInBase.TrimEnd('/'),
            required.SignInUrl.AbsoluteUri,
            StringComparison.Ordinal);
        Assert.Equal(required, failure.Requirement);
        await AssertJournalHasNoSecretsAsync(auth, required.State, cancellationToken);
    }

    [Fact(DisplayName =
        "expired token uses the same authorization-required rail")]
    public async Task ExpiredTokenParksAsAuthorizationRequired()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(test);

        var seedCommand = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var driver = test.Neuron<IIntegrationDriver>("auth-expired");

        var seedRequired = await Assert.ThrowsAsync<McpAuthorizationRequiredException>(() =>
            driver.Reference.ReadGmailMessage(
                seedCommand,
                IntegrationsFixture.SampleGmailAccount,
                IntegrationsFixture.SampleMessageId,
                cancellationToken));
        Assert.NotNull(seedRequired.Requirement);

        await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(seedRequired.Requirement.State, "seed-code", Error: null, Iss: null),
            cancellationToken);
        var seeded = await driver.Reference.ReadGmailMessage(
            seedCommand,
            IntegrationsFixture.SampleGmailAccount,
            IntegrationsFixture.SampleMessageId,
            cancellationToken);
        Assert.Equal(IntegrationsFixture.SampleMessageId, seeded.Id);

        await test.Clock.AdvanceAsync(TimeSpan.FromHours(2), cancellationToken);

        var expiredCommand = CommandId.New();
        var failure = await Assert.ThrowsAsync<McpAuthorizationRequiredException>(() =>
            driver.Reference.ReadGmailMessage(
                expiredCommand,
                IntegrationsFixture.SampleGmailAccount,
                IntegrationsFixture.SampleMessageId,
                cancellationToken));

        Assert.NotNull(failure.Requirement);
        Assert.Equal(expiredCommand, failure.Requirement.CommandId);
        Assert.NotEqual(seedRequired.Requirement.State, failure.Requirement.State);
        var requiredFacts = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        Assert.Equal(2, requiredFacts.Count);
        Assert.Contains(requiredFacts, observed => observed.Synapse == failure.Requirement);
    }

    [Fact(DisplayName =
        "denied authorization journals AuthorizationDenied and re-issued command fails typed without looping")]
    public async Task DeniedAuthorizationFailsTypedOnReissueWithoutLoop()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(test);

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var deniedWait = auth.Outgoing.NextAsync<AuthorizationDenied>(cancellationToken);
        var driver = test.Neuron<IIntegrationDriver>("auth-denied");

        var required = await Assert.ThrowsAsync<McpAuthorizationRequiredException>(() =>
            driver.Reference.ReadGmailMessage(
                commandId,
                IntegrationsFixture.SampleGmailAccount,
                IntegrationsFixture.SampleMessageId,
                cancellationToken));
        Assert.NotNull(required.Requirement);

        var delivery = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(
                required.Requirement.State,
                Code: null,
                Error: "access_denied",
                Iss: null),
            cancellationToken);

        Assert.True(delivery.Accepted);
        Assert.True(delivery.Denied);
        Assert.False(delivery.Completed);

        var denied = (await deniedWait).Synapse;
        Assert.Equal(commandId, denied.CommandId);
        Assert.Equal(required.Requirement.State, denied.State);

        var reissued = await Assert.ThrowsAsync<McpAuthorizationDeniedException>(() =>
            driver.Reference.ReadGmailMessage(
                commandId,
                IntegrationsFixture.SampleGmailAccount,
                IntegrationsFixture.SampleMessageId,
                cancellationToken));

        Assert.Equal(denied, reissued.Denial);
        var requiredAgain = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        Assert.Single(requiredAgain);
    }

    [Fact(DisplayName =
        "completed authorization delivers code through the edge seam, caches the token, journals AuthorizationCompleted, and re-issues the original CommandId")]
    public async Task CompletedAuthorizationResumesOriginalCommand()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(test);

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var completedWait = auth.Outgoing.NextAsync<AuthorizationCompleted>(cancellationToken);
        var driver = test.Neuron<IIntegrationDriver>("auth-completed");

        var required = await Assert.ThrowsAsync<McpAuthorizationRequiredException>(() =>
            driver.Reference.ReadGmailMessage(
                commandId,
                IntegrationsFixture.SampleGmailAccount,
                IntegrationsFixture.SampleMessageId,
                cancellationToken));
        Assert.NotNull(required.Requirement);

        var delivery = await auth.Reference.DeliverCallback(
            new DeliverMcpAuthorizationCallback(
                required.Requirement.State,
                Code: "edge-delivered-code",
                Error: null,
                Iss: "https://accounts.google.com"),
            cancellationToken);

        Assert.True(delivery.Accepted);
        Assert.True(delivery.Completed);
        Assert.False(delivery.Denied);

        var completed = (await completedWait).Synapse;
        Assert.Equal(commandId, completed.CommandId);
        Assert.Equal(required.Requirement.State, completed.State);

        var message = await driver.Reference.ReadGmailMessage(
            commandId,
            IntegrationsFixture.SampleGmailAccount,
            IntegrationsFixture.SampleMessageId,
            cancellationToken);

        Assert.Equal(
            new GmailMessage(
                IntegrationsFixture.SampleMessageId,
                IntegrationsFixture.SampleSubject,
                IntegrationsFixture.SampleSender,
                IntegrationsFixture.SampleBody),
            message);
        await AssertJournalHasNoSecretsAsync(auth, required.Requirement.State, cancellationToken);
        Assert.Single(await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken));
        Assert.Single(await auth.Outgoing.ReadAsync<AuthorizationCompleted>(afterSequence: 0, cancellationToken));
    }

    [Fact(DisplayName =
        "state mismatch at the callback is rejected and nothing is journaled as completed")]
    public async Task StateMismatchIsRejectedWithoutCompletion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(test);

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var driver = test.Neuron<IIntegrationDriver>("auth-mismatch");

        _ = await Assert.ThrowsAsync<McpAuthorizationRequiredException>(() =>
            driver.Reference.ReadGmailMessage(
                commandId,
                IntegrationsFixture.SampleGmailAccount,
                IntegrationsFixture.SampleMessageId,
                cancellationToken));

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
    }

    private static void CatalogGmail(TestBrain test)
        => test.Mcp().Catalog(
            IntegrationsFixture.GmailServerKey,
            AdmittedMcpTools.GmailGetMessage(
                id: IntegrationsFixture.SampleMessageId,
                subject: IntegrationsFixture.SampleSubject,
                sender: IntegrationsFixture.SampleSender,
                plaintextBody: IntegrationsFixture.SampleBody));

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
}
