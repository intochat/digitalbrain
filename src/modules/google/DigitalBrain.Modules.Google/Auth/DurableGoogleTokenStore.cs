using System.Text;
using DigitalBrain.Security;
using Google.Apis.Json;
using Google.Apis.Util.Store;
using Orleans.Journaling;

namespace DigitalBrain.Google.Auth;

internal sealed class DurableGoogleTokenStore(
    IDurableValue<byte[]> state,
    Func<ValueTask> commit,
    IDurablePayloadProtector protector,
    string purpose) : IDataStore
{
    public static string Purpose(string connectionName, string durableIdentity)
        => $"google/oauth/{connectionName}/{durableIdentity}";

    public async Task StoreAsync<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        var entries = ReadEntries();
        entries[StoredKey(key, typeof(T))] = NewtonsoftJsonSerializer.Instance.Serialize(value);
        await WriteEntriesAsync(entries).ConfigureAwait(false);
    }

    public Task DeleteAsync<T>(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var entries = ReadEntries();
        if (!entries.Remove(StoredKey(key, typeof(T))))
        {
            return Task.CompletedTask;
        }

        return WriteEntriesAsync(entries);
    }

    public Task<T> GetAsync<T>(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var entries = ReadEntries();
        if (!entries.TryGetValue(StoredKey(key, typeof(T)), out var serialized)
            || string.IsNullOrEmpty(serialized))
        {
            return Task.FromResult(default(T)!);
        }

        return Task.FromResult(NewtonsoftJsonSerializer.Instance.Deserialize<T>(serialized));
    }

    public Task ClearAsync()
    {
        if (state.Value is not { Length: > 0 })
        {
            return Task.CompletedTask;
        }

        return WriteEntriesAsync(new Dictionary<string, string>(StringComparer.Ordinal));
    }

    private Dictionary<string, string> ReadEntries()
    {
        if (state.Value is not { Length: > 0 } protectedPayload)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var plaintext = protector.Unprotect(purpose, protectedPayload);
        if (plaintext.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var entries = NewtonsoftJsonSerializer.Instance.Deserialize<Dictionary<string, string>>(
            Encoding.UTF8.GetString(plaintext));
        return entries is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(entries, StringComparer.Ordinal);
    }

    private async Task WriteEntriesAsync(Dictionary<string, string> entries)
    {
        var previous = state.Value;
        if (entries.Count == 0)
        {
            state.Value = [];
        }
        else
        {
            var serialized = Encoding.UTF8.GetBytes(NewtonsoftJsonSerializer.Instance.Serialize(entries));
            state.Value = protector.Protect(purpose, serialized);
        }

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

    private static string StoredKey(string key, Type type)
        => $"{type.FullName}-{key}";
}
