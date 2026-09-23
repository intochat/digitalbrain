namespace DigitalBrain.Inbox;

public interface IEmailSender
{
    Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default);
}

public sealed record SentEmail(string Recipient, string Subject, string Body);

public sealed class InMemoryEmailSender : IEmailSender
{
    private readonly List<SentEmail> _sent = [];

    public IReadOnlyList<SentEmail> Sent => _sent;

    public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
    {
        _sent.Add(new SentEmail(recipient, subject, body));
        return Task.CompletedTask;
    }
}
