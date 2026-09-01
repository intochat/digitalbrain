using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Google;

internal sealed class FakeGmail : IGmail
{
    public Task<string> SearchJsonAsync(OwnerId owner, string account, string topic, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            """{"threads":[{"id":"thread-intochat","messages":[{"id":"message-intochat","subject":"New Customer","snippet":"Please send company information.","sender":"vlad@intochat.io"}]}]}""");
    }
}
