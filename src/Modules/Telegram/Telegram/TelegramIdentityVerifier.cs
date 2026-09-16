using System.Globalization;
using DigitalBrain.Identity;

namespace DigitalBrain.Telegram;

/// <summary>Translates Telegram's signed Mini App proof into an immutable external identity.</summary>
public sealed class TelegramIdentityVerifier(TelegramOptions options, TimeProvider clock) : IExternalIdentityVerifier
{
    public string Provider => "telegram";

    public ValueTask<VerifiedExternalIdentity?> VerifyAsync(string credential, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(options.BotToken)
            || !TelegramAuthentication.TryValidateInitData("tma " + credential, options.BotToken, clock.GetUtcNow(), out var userId))
        {
            return ValueTask.FromResult<VerifiedExternalIdentity?>(null);
        }
        return ValueTask.FromResult<VerifiedExternalIdentity?>(new(Provider, userId.ToString(CultureInfo.InvariantCulture)));
    }
}
