namespace DigitalBrain.SDK.Google.Auth;

public interface ITokenBlob : IGrainWithStringKey
{
    Task<byte[]?> GetAsync();
    Task SetAsync(byte[] encrypted);
    Task ClearAsync();
}

public sealed class TokenBlobGrain(
    [PersistentState("token", "digitalbrain")] IPersistentState<TokenBlobState> state)
    : Grain, ITokenBlob
{
    public Task<byte[]?> GetAsync() => Task.FromResult(state.State.Bytes);

    public async Task SetAsync(byte[] encrypted)
    {
        state.State.Bytes = encrypted;
        await state.WriteStateAsync();
    }

    public async Task ClearAsync()
    {
        state.State.Bytes = null;
        await state.WriteStateAsync();
    }
}

[GenerateSerializer]
public sealed class TokenBlobState
{
    [Id(0)] public byte[]? Bytes { get; set; }
}
