using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace DigitalBrain.Core;

public sealed class TokenHandoff(TimeProvider clock)
{
    private readonly Dictionary<string, (OAuthTokens Tokens, DateTimeOffset ExpiresAt)> _pending = new(StringComparer.Ordinal);

    public string Deposit(OAuthTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        lock (_pending)
        {
            var now = clock.GetUtcNow();
            foreach (var nonce in _pending.Where(entry => entry.Value.ExpiresAt <= now).Select(entry => entry.Key).ToArray())
            {
                _pending.Remove(nonce);
            }
            if (_pending.Count >= 128)
            {
                throw new InvalidOperationException("Too many pending OAuth logins. Wait two minutes and sign in again.");
            }
            var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            _pending.Add(key, (tokens, now.AddMinutes(2)));
            return key;
        }
    }

    public bool TryPeek(string nonce, [MaybeNullWhen(false)] out OAuthTokens tokens)
    {
        lock (_pending)
        {
            if (nonce is not null && _pending.TryGetValue(nonce, out var pending) && pending.ExpiresAt > clock.GetUtcNow())
            {
                tokens = pending.Tokens;
                return true;
            }
            tokens = null;
            return false;
        }
    }

    public void Consume(string nonce)
    {
        lock (_pending)
        {
            _pending.Remove(nonce);
        }
    }
}
