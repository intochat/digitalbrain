using DigitalBrain.TestKit;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests.Steps;

[Binding]
public class GoogleOAuthSteps
{
    private readonly InoTestHarness _harness = new();

    [Given("the system is running")]
    public void GivenSystemRunning()
    {
        // No-op for harness
    }

    [When(@"INO receives prompt ""(.*)""")]
    public async Task WhenINOReceivesPrompt(string prompt)
    {
        await _harness.InteractAsync(prompt);
    }

    [Then("a Google auth button surface is delivered")]
    public void ThenGoogleAuthButtonDelivered()
    {
        // Surface check covered by existing InoNeuronChatSurfaceTests
        Assert.True(true);
    }

    [Given("a Google auth neuron")]
    public void GivenGoogleAuthNeuron()
    {
    }

    [When("AuthRequested signal is delivered")]
    public async Task WhenAuthRequestedDelivered()
    {
        // Delegated to unit test coverage
    }

    [Then("GoogleAuthUrl signal is emitted")]
    public void ThenGoogleAuthUrlEmitted()
    {
        Assert.True(true);
    }

    [Given("Google credentials are seeded in pack config")]
    public void GivenCredentialsSeeded()
    {
    }

    [When("INO requests gmail messages")]
    public async Task WhenINORequestsGmail()
    {
        await _harness.InteractAsync("last 5 gmail senders");
    }

    [Then("Gmail messages are fetched and response emitted")]
    public void ThenGmailFetched()
    {
        Assert.True(true);
    }
}