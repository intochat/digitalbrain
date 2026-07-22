using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ModelContextProtocol.Authentication;
using Orleans.Journaling;

namespace DigitalBrain.Google;

internal sealed class DurableMcpTokenCache(
    IDurableValue<byte[]> state,
    Func<ValueTask> commit,
    IDataProtector protector) : ITokenCache
{
    public ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (state.Value is not { Length: > 0 } protectedTokens)
        {
            return ValueTask.FromResult<TokenContainer?>(null);
        }

        var serialized = protector.Unprotect(protectedTokens);
        return ValueTask.FromResult(JsonSerializer.Deserialize<TokenContainer>(serialized));
    }

    public async ValueTask StoreTokensAsync(
        TokenContainer tokens,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        cancellationToken.ThrowIfCancellationRequested();

        state.Value = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(tokens));
        await commit();
    }
}
