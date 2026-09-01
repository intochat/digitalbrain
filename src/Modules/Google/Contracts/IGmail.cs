using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Google;

public interface IGmail
{
    Task<string> SearchJsonAsync(OwnerId owner, string account, string topic, CancellationToken cancellationToken);
}
