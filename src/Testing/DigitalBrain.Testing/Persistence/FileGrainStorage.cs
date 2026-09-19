using System.Security.Cryptography;
using System.Text;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;
namespace DigitalBrain.Testing;

// Single-owner test store. The host holds an exclusive directory lease.
internal sealed class FileGrainStorage(string directory, Serializer serializer, SemaphoreSlim? sharedGate = null) : IGrainStorage
{
    private readonly SemaphoreSlim _gate = sharedGate ?? new(1);
    public async Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> state)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var path = PathFor(stateName, grainId);
            if (!File.Exists(path))
            {
                state.State = Activator.CreateInstance<T>();
                state.ETag = null; state.RecordExists = false;
                return;
            }
            var (etag, payload) = Decode(await File.ReadAllBytesAsync(path).ConfigureAwait(false));
            state.State = serializer.Deserialize<T>(payload);
            state.ETag = etag; state.RecordExists = true;
        }
        finally { _gate.Release(); }
    }
    public async Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> state)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var path = PathFor(stateName, grainId);
            await CheckEtag(path, state.ETag).ConfigureAwait(false);
            var etag = Guid.NewGuid().ToString("N");
            var tag = Encoding.UTF8.GetBytes(etag);
            var payload = serializer.SerializeToArray(state.State);
            var bytes = new byte[4 + tag.Length + payload.Length];
            BitConverter.GetBytes(tag.Length).CopyTo(bytes, 0);
            tag.CopyTo(bytes, 4); payload.CopyTo(bytes, 4 + tag.Length);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes).ConfigureAwait(false);
                File.Move(temporary, path, overwrite: true);
            }
            finally { File.Delete(temporary); }
            state.ETag = etag; state.RecordExists = true;
        }
        finally { _gate.Release(); }
    }
    public async Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> state)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var path = PathFor(stateName, grainId);
            await CheckEtag(path, state.ETag).ConfigureAwait(false);
            File.Delete(path);
            state.State = Activator.CreateInstance<T>(); state.ETag = null; state.RecordExists = false;
        }
        finally { _gate.Release(); }
    }
    private static async Task CheckEtag(string path, string? expected)
    {
        var actual = File.Exists(path) ? Decode(await File.ReadAllBytesAsync(path).ConfigureAwait(false)).Etag : null;
        if (actual != expected) { throw new InconsistentStateException("Test storage ETag conflict."); }
    }
    private static (string Etag, byte[] Payload) Decode(byte[] bytes)
    {
        if (bytes.Length < 4) { throw new InvalidDataException("Truncated state."); }
        var length = BitConverter.ToInt32(bytes, 0);
        if (length <= 0 || length > bytes.Length - 4) { throw new InvalidDataException("Invalid state header."); }
        return (Encoding.UTF8.GetString(bytes, 4, length), bytes[(4 + length)..]);
    }
    private string PathFor(string stateName, GrainId id)
        => Path.Combine(directory, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stateName + "\0" + id))) + ".state");
}
