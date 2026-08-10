using System.Text.Json;
using DigitalBrain.Security;
using ModelContextProtocol.Authentication;
using Orleans.Journaling;

namespace DigitalBrain.Modules.Sdk.Mcp;

internal sealed class DurableMcpTokenCache(
    IDurableValue<byte[]> state,
    Func<ValueTask> commit,
    IDurablePayloadProtector protector,
    string purpose) : ITokenCache
{
    public ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (state.Value is not { Length: > 0 } protectedTokens)
        {
            return ValueTask.FromResult<TokenContainer?>(null);
        }

        var serialized = protector.Unprotect(purpose, protectedTokens);
        var tokens = JsonSerializer.Deserialize<TokenContainer>(serialized)
            ?? throw new InvalidOperationException("The durable MCP token payload is empty.");
        return ValueTask.FromResult<TokenContainer?>(tokens);
    }

    public async ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        cancellationToken.ThrowIfCancellationRequested();

        var previous = state.Value;
        state.Value = protector.Protect(purpose, JsonSerializer.SerializeToUtf8Bytes(tokens));

        try
        {
            await commit().ConfigureAwait(false);
        }
        catch
        {
            state.Value = previous;
            throw;
        }
    }
}
