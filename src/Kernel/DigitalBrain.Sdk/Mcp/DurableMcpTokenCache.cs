using System.Text.Json;
using DigitalBrain.Modules.Sdk;
using ModelContextProtocol.Authentication;
using Orleans.Journaling;

namespace DigitalBrain.Modules.Sdk.Mcp;

internal sealed class DurableMcpTokenCache : ITokenCache
{
    private readonly Func<byte[]?> _read;
    private readonly Action<byte[]?> _write;
    private readonly Func<ValueTask> _commit;
    private readonly IDurablePayloadProtector _protector;
    private readonly string _purpose;

    internal DurableMcpTokenCache(
        IDurableValue<byte[]> state,
        Func<ValueTask> commit,
        IDurablePayloadProtector protector,
        string purpose)
        : this(
            () => state.Value is { Length: > 0 } bytes ? bytes : null,
            value => state.Value = value ?? [],
            commit,
            protector,
            purpose)
    {
    }

    internal DurableMcpTokenCache(
        PrincipalTokenSlot slot,
        Func<ValueTask> commit,
        IDurablePayloadProtector protector,
        string purpose)
        : this(slot.Read, slot.Write, commit, protector, purpose)
    {
    }

    private DurableMcpTokenCache(
        Func<byte[]?> read,
        Action<byte[]?> write,
        Func<ValueTask> commit,
        IDurablePayloadProtector protector,
        string purpose)
    {
        _read = read;
        _write = write;
        _commit = commit;
        _protector = protector;
        _purpose = purpose;
    }

    public ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_read() is not { Length: > 0 } protectedTokens)
        {
            return ValueTask.FromResult<TokenContainer?>(null);
        }

        var serialized = _protector.Unprotect(_purpose, protectedTokens);
        var tokens = JsonSerializer.Deserialize<TokenContainer>(serialized)
            ?? throw new InvalidOperationException("The durable MCP token payload is empty.");
        return ValueTask.FromResult<TokenContainer?>(tokens);
    }

    public async ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        cancellationToken.ThrowIfCancellationRequested();

        var previous = _read();
        _write(_protector.Protect(_purpose, JsonSerializer.SerializeToUtf8Bytes(tokens)));

        try
        {
            await _commit().ConfigureAwait(false);
        }
        catch
        {
            _write(previous);
            throw;
        }
    }
}
