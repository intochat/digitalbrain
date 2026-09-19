using DigitalBrain.Contracts;
using DigitalBrain.Core;

namespace DigitalBrain.Testing;

public interface IGmail : INeuron
{
    Task Send(string to, string subject, string body);
    Task<IReadOnlyList<EmailSent>> Outbox();
}

public interface IMailLog : INeuron
{
    Task Append(EmailSent email);
    Task<IReadOnlyList<EmailSent>> Read();
}

[GenerateSerializer, Alias("gmail.sent")]
public sealed record EmailSent(
    [property: Id(0)] string From,
    [property: Id(1)] string To,
    [property: Id(2)] string Subject,
    [property: Id(3)] string Body) : Signal;

[GrainType("gmail")]
public sealed class Gmail : Neuron, IGmail
{
    private readonly List<EmailSent> _outbox = [];

    public async Task Send(string to, string subject, string body)
    {
        var sent = new EmailSent(this.GetPrimaryKeyString(), to, subject, body);
        _outbox.Add(sent);
        await Broadcast(sent);
    }

    public Task<IReadOnlyList<EmailSent>> Outbox() => Task.FromResult<IReadOnlyList<EmailSent>>(_outbox.ToArray());
}

[GrainType("mail-log")]
public sealed class MailLog : Neuron, IMailLog
{
    private readonly List<EmailSent> _log = [];

    public Task Append(EmailSent email)
    {
        _log.Add(email);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EmailSent>> Read() => Task.FromResult<IReadOnlyList<EmailSent>>(_log.ToArray());
}
