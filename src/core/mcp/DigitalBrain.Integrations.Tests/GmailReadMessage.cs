using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using DigitalBrain.Mcp.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class GmailReadMessage(IntegrationsFixture fixture)
{
    [Fact(DisplayName =
        "GmailRequest returns GmailMessage vocabulary on the scripted edge")]
    public async Task ReadMessageReturnsGmailMessageVocabulary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        var response = await GmailHelpers.SendReadIntentAsync(
            test,
            CommandId.New(),
            IntegrationsFixture.SampleGmailAccount,
            IntegrationsFixture.SampleMessageId,
            cancellationToken);

        Assert.Equal(
            new GmailMessage(
                IntegrationsFixture.SampleMessageId,
                IntegrationsFixture.SampleSubject,
                IntegrationsFixture.SampleSender,
                IntegrationsFixture.SampleBody),
            Assert.Single(response.Messages));
    }

    [Fact(DisplayName =
        "GmailRequest fails closed when Gmail provider returns an error")]
    public async Task ReadMessageFailsClosedWhenEdgeIsNotAdmitted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string messageId = "msg-provider-error-1";
        IntegrationsGmailHosts.GmailHost.SeedMessage(
            messageId,
            IntegrationsFixture.SampleSubject,
            IntegrationsFixture.SampleSender,
            IntegrationsFixture.SampleBody);
        IntegrationsGmailHosts.GmailHost.SetGetStatus(messageId, System.Net.HttpStatusCode.ServiceUnavailable);
        await GmailHelpers.SeedAuthorizationAsync(test, cancellationToken: cancellationToken);
        GmailHelpers.ScriptReadSampleMessage(test, messageId);

        var response = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(new GmailRequest($"Read Gmail message {messageId}"), cancellationToken);

        Assert.False(response.Succeeded);
        Assert.Contains("Gmail", response.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("error", response.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName =
        "GmailRequest rejects a Gmail response for a different requested message")]
    public async Task ReadMessageRejectsMismatchedResponseId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string messageId = "msg-mismatch-request-1";
        IntegrationsGmailHosts.GmailHost.SeedMessage(
            messageId,
            IntegrationsFixture.SampleSubject,
            IntegrationsFixture.SampleSender,
            IntegrationsFixture.SampleBody,
            responseId: "msg-different");
        await GmailHelpers.SeedAuthorizationAsync(test, cancellationToken: cancellationToken);
        GmailHelpers.ScriptReadSampleMessage(test, messageId);

        var response = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(new GmailRequest($"Read Gmail message {messageId}"), cancellationToken);

        Assert.False(response.Succeeded);
        Assert.Equal(
            $"Gmail get_message returned id 'msg-different' for requested message '{messageId}'.",
            response.Error);
    }

    [Fact(DisplayName =
        "GmailRequest rejects a Gmail response that omits its message identifier")]
    public async Task ReadMessageRejectsMissingResponseId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string messageId = "msg-missing-id-1";
        IntegrationsGmailHosts.GmailHost.SeedMessageMissingId(
            messageId,
            IntegrationsFixture.SampleSubject,
            IntegrationsFixture.SampleSender,
            IntegrationsFixture.SampleBody);
        await GmailHelpers.SeedAuthorizationAsync(test, cancellationToken: cancellationToken);
        GmailHelpers.ScriptReadSampleMessage(test, messageId);

        var response = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(new GmailRequest($"Read Gmail message {messageId}"), cancellationToken);

        Assert.False(response.Succeeded);
        Assert.Equal("Gmail get_message returned no id.", response.Error);
    }

    [Fact(DisplayName =
        "GmailRequest returns the requested Gmail message when its subject and body are empty")]
    public async Task ReadMessageAllowsEmptySubjectAndBody()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string messageId = "msg-empty-body-1";
        IntegrationsGmailHosts.GmailHost.SeedMessage(
            messageId,
            subject: string.Empty,
            sender: IntegrationsFixture.SampleSender,
            plaintextBody: string.Empty);
        await GmailHelpers.SeedAuthorizationAsync(test, cancellationToken: cancellationToken);
        GmailHelpers.ScriptReadSampleMessage(test, messageId);

        var response = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {messageId}", CommandId.New()),
                cancellationToken);

        Assert.Equal(
            new GmailMessage(
                messageId,
                string.Empty,
                IntegrationsFixture.SampleSender,
                string.Empty),
            Assert.Single(response.Messages));
    }

    [Fact(DisplayName =
        "GmailRequest rejects a Gmail provider error as a typed response")]
    public async Task ReadMessageRejectsToolError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string messageId = "msg-tool-error-1";
        IntegrationsGmailHosts.GmailHost.SeedMessage(
            messageId,
            IntegrationsFixture.SampleSubject,
            IntegrationsFixture.SampleSender,
            IntegrationsFixture.SampleBody);
        IntegrationsGmailHosts.GmailHost.SetGetStatus(messageId, System.Net.HttpStatusCode.InternalServerError);
        await GmailHelpers.SeedAuthorizationAsync(test, cancellationToken: cancellationToken);
        GmailHelpers.ScriptReadSampleMessage(test, messageId);

        var response = await test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(new GmailRequest($"Read Gmail message {messageId}"), cancellationToken);

        Assert.False(response.Succeeded);
        Assert.Contains("Gmail", response.Error, StringComparison.Ordinal);
        Assert.Contains("error", response.Error, StringComparison.OrdinalIgnoreCase);
    }
}
