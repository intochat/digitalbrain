using System.Text;
using System.Text.Json;
using DigitalBrain.Runtime;
using Google.Apis.Util.Store;

namespace DigitalBrain.SDK.Google.Auth;

public sealed class DigitalBrainGrainDataStore(
    IGrainFactory grains,
    ITokenProtector protector,
    string userAccountId) : IDataStore
{
    public async Task StoreAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var encrypted = protector.Protect(Encoding.UTF8.GetBytes(json));
        await grains.GetGrain<ITokenBlob>(MakeKey(key)).SetAsync(encrypted);
    }

    public async Task<T> GetAsync<T>(string key)
    {
        var blob = await grains.GetGrain<ITokenBlob>(MakeKey(key)).GetAsync();
        if (blob is null) return default!;
        var plaintext = Encoding.UTF8.GetString(protector.Unprotect(blob));
        return JsonSerializer.Deserialize<T>(plaintext)!;
    }

    public Task DeleteAsync<T>(string key) =>
        grains.GetGrain<ITokenBlob>(MakeKey(key)).ClearAsync();

    public Task ClearAsync() => Task.CompletedTask;

    string MakeKey(string googleKey)
    {
        var brainId = BrainScopeHelper.GetActiveScope();
        return $"{brainId}:{userAccountId}:{googleKey}";
    }
}
