using DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer.GitHub;

[GrainType("DigitalBrain.Developer.GitHubCredentialStore")]
public sealed class GitHubCredentialStoreGrain(
    [PersistentState("github-token", "digitalbrain")] IPersistentState<GitHubCredentialState> state)
    : Grain, IGitHubCredentialStore
{
    public Task SetEncryptedTokenAsync(byte[] encryptedToken)
    {
        state.State.Bytes = encryptedToken;
        return state.WriteStateAsync();
    }

    public Task<byte[]?> GetEncryptedTokenAsync()
    {
        return Task.FromResult(state.State.Bytes);
    }

    public async Task ClearTokenAsync()
    {
        state.State.Bytes = null;
        await state.WriteStateAsync();
    }
}

[GenerateSerializer]
public sealed class GitHubCredentialState
{
    [Id(0)] public byte[]? Bytes { get; set; }
}
