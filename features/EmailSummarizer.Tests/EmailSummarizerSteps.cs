using DigitalBrain.Features.EmailSummarizer;
using DigitalBrain.Features.Sdk;
using DigitalBrain.Features.Testing;
using DigitalBrain.Integrations.Google.Contracts;
using Reqnroll;
using Xunit;
using EmailSummarizerImplementation = DigitalBrain.Features.EmailSummarizer.EmailSummarizerFeature;

namespace DigitalBrain.Features.EmailSummarizer.Tests;

[Binding]
public sealed class EmailSummarizerSteps(
    FeatureScenarioContext scenario,
    GeneratedFeatureScenario generatedScenario)
{
    private GmailMessage? _message;

    [Given("Gmail message {string} has subject {string} and body {string}")]
    public void GivenGmailMessageHasSubjectAndBody(string messageId, string subject, string body)
    {
        _message = new GmailMessage(
            messageId,
            null,
            DateTimeOffset.UnixEpoch,
            "sender@example.com",
            subject,
            body);
        scenario.ConfigureMessage(_message);
    }

    [Given("Gmail message reads are granted")]
    public void GivenGmailMessageReadsAreGranted()
    {
        scenario.SetGmailReadGrant(true);
    }

    [Given("Gmail message reads are not granted")]
    public void GivenGmailMessageReadsAreNotGranted()
    {
        scenario.SetGmailReadGrant(false);
    }

    [Given("model workflow {string} for input {string} returns {string}")]
    public void GivenModelWorkflowReturns(string workflowId, string inputId, string response)
    {
        var message = _message ?? throw new InvalidOperationException("Configure a Gmail message first.");
        var body = message.PlainTextBody.Length <= 16_384
            ? message.PlainTextBody
            : message.PlainTextBody[..16_384];
        var prompt = $"Summarize this email.\nSubject: {message.Subject}\nBody: {body}";
        scenario.ConfigureModelResponse(new ModelRequest(workflowId, prompt, "generate-summary"), response);
    }

    [When("feature input {string} requests a summary of Gmail message {string}")]
    public Task WhenFeatureInputRequestsASummaryOfGmailMessage(string inputId, string messageId) =>
        ExecuteAsync(inputId, messageId);

    [When("feature input {string} requests a summary of Gmail message {string} twice")]
    public async Task WhenFeatureInputRequestsASummaryOfGmailMessageTwice(string inputId, string messageId)
    {
        await ExecuteAsync(inputId, messageId);
        await ExecuteAsync(inputId, messageId);
    }

    [Then("exactly one text surface intent contains {string}")]
    public void ThenExactlyOneTextSurfaceIntentContains(string text)
    {
        var surface = Assert.Single(scenario.Surfaces);
        Assert.Contains(text, surface.Text, StringComparison.Ordinal);
    }

    [Then("the Gmail reader and model workflow each ran once")]
    public void ThenTheGmailReaderAndModelWorkflowEachRanOnce()
    {
        Assert.Equal(1, scenario.GmailReadCount);
        Assert.Equal(1, scenario.ModelCallCount);
    }

    [Then("the model and surface use distinct stable operation keys")]
    public void ThenTheModelAndSurfaceUseDistinctStableOperationKeys()
    {
        Assert.Equal("generate-summary", Assert.Single(scenario.ModelRequests).LogicalOperationKey);
        Assert.Equal("publish-summary", Assert.Single(scenario.Surfaces).LogicalOperationKey);
    }

    [BeforeScenario("generated-duplicate")]
    public void ConfigureGeneratedDuplicateScenario()
    {
        scenario.Reset();
        var message = new GmailMessage(
            "generated-message",
            null,
            DateTimeOffset.UnixEpoch,
            "sender@example.com",
            "Generated duplicate",
            "This message must be summarized once.");
        scenario.ConfigureMessage(message);
        scenario.SetGmailReadGrant(true);
        var prompt = $"Summarize this email.\nSubject: {message.Subject}\nBody: {message.PlainTextBody}";
        scenario.ConfigureModelResponse(
            new ModelRequest("email-summary", prompt, "generate-summary"),
            "Summarized once.");
        generatedScenario.Configure(
            new EmailSummarizerImplementation(scenario.GmailReader),
            new FeatureInput(
                "generated-input",
                "gmail.message.summary.requested.v1",
                DateTimeOffset.UnixEpoch,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["messageId"] = message.MessageId
                }));
    }

    [Then("no model workflow or surface intent ran")]
    public void ThenNoModelWorkflowOrSurfaceIntentRan()
    {
        Assert.Equal(0, scenario.ModelCallCount);
        Assert.Empty(scenario.Surfaces);
    }

    [Then("no surface intent was emitted")]
    public void ThenNoSurfaceIntentWasEmitted()
    {
        Assert.Empty(scenario.Surfaces);
    }

    private Task<FeatureScenarioResult> ExecuteAsync(string inputId, string messageId)
    {
        var feature = new EmailSummarizerImplementation(scenario.GmailReader);
        var input = new FeatureInput(
            inputId,
            "gmail.message.summary.requested.v1",
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["messageId"] = messageId
            });
        return scenario.ExecuteAsync(feature, input);
    }
}
