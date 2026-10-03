using System.Diagnostics.CodeAnalysis;
namespace DigitalBrain.Platform.Contracts.Auth;
public interface ITokenHandoff
{
    string Deposit(OAuthTokens tokens);
    bool TryPeek(string nonce, [MaybeNullWhen(false)] out OAuthTokens tokens);
    void Consume(string nonce);
}
