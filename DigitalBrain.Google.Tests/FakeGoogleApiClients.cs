namespace DigitalBrain.Google.Tests;

public sealed class FakeGmailApiClient : IGmailApiClient
{
    public List<(string To, string Subject, string Body)> SentMessages { get; } = [];

    public Task<string[]> ListMessagesAsync(string query, int maxResults, CancellationToken ct) =>
        Task.FromResult(new[] { "fake-message-1", "fake-message-2" });

    public Task<string> ReadMessageAsync(string messageId, CancellationToken ct) =>
        Task.FromResult($"fake body for {messageId}");

    public Task SendMessageAsync(string to, string subject, string body, CancellationToken ct)
    {
        SentMessages.Add((to, subject, body));
        return Task.CompletedTask;
    }
}
