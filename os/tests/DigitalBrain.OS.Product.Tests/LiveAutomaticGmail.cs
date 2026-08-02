using System.Globalization;
using System.Text.Json;
using Xunit;

namespace DigitalBrain.ProductTests;

[Collection("live product")]
public sealed class LiveAutomaticGmail
{
    [Fact(
        Explicit = true,
        Timeout = 900_000,
        DisplayName =
            "LIVE product: Gmail intent via chat requires owner OAuth secrets; records blocker when credentials absent")]
    public async Task GmailIntentIsObservableOrBlockedByMissingSecrets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = LiveProductAspire.FindRepositoryRoot();
        var chatName = $"gmail-verification-{Guid.NewGuid():N}";
        var commandId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        const string prompt = "Read my last three emails.";

        await LiveProductAspire.RunScenarioAsync(
            repository,
            ["silo", LiveProductAspire.McpResource, "brain-ai-gemma4"],
            async () =>
            {
                try
                {
                    var result = await LiveProductAspire.CallToolAsync(
                        repository,
                        "send_chat_message",
                        JsonSerializer.Serialize(
                            new
                            {
                                text = prompt,
                                commandId,
                                chatName,
                                timeoutSeconds = 300,
                            }),
                        cancellationToken);

                    var response = LiveProductJson.RequiredString(result, "response");
                    Assert.False(string.IsNullOrWhiteSpace(response));

                    var transcript = await LiveProductAspire.CallToolAsync(
                        repository,
                        "read_chat_transcript",
                        JsonSerializer.Serialize(new { chatName }),
                        cancellationToken);
                    var turns = LiveProductJson.RequiredArray(transcript, "turns");
                    Assert.NotEmpty(turns);

                    var neurons = await LiveProductAspire.CallToolAsync(
                        repository,
                        "list_active_neurons",
                        "{}",
                        cancellationToken);
                    Assert.NotNull(neurons);
                }
                catch (InvalidOperationException failure)
                    when (IsCredentialBlocker(failure.Message))
                {
                    Assert.Fail(
                        "LIVE Gmail proof blocked by missing owner OAuth/Gmail credentials or authorization rail: "
                        + failure.Message);
                }
            },
            cancellationToken);
    }

    private static bool IsCredentialBlocker(string message)
        => message.Contains("oauth", StringComparison.OrdinalIgnoreCase)
            || message.Contains("authorization", StringComparison.OrdinalIgnoreCase)
            || message.Contains("credential", StringComparison.OrdinalIgnoreCase)
            || message.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || message.Contains("sign-in", StringComparison.OrdinalIgnoreCase)
            || message.Contains("signin", StringComparison.OrdinalIgnoreCase)
            || message.Contains("gmail", StringComparison.OrdinalIgnoreCase);
}
