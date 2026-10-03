using System.Diagnostics.CodeAnalysis;
namespace DigitalBrain.Platform.Contracts.Auth;
public interface ITokenHandoff
{
    string Deposit(OAuthTokens tokens);
    bool TryTake(string nonce, [MaybeNullWhen(false)] out OAuthTokens tokens);
}
