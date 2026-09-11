using System.Security.Cryptography;
using System.Text;
using Orleans;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;

namespace DigitalBrain.Testing;

internal sealed class FileGrainStorage : IGrainStorage
{
    private readonly string _directory;
    private readonly Serializer _serializer;

    public FileGrainStorage(string directory, Serializer serializer)
    {
        _directory = Path.GetFullPath(directory);
        _serializer = serializer;
        Directory.CreateDirectory(_directory);
    }

    public async Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        var path = PathFor(stateName, grainId);
        if (File.Exists(path))
        {
            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            var etagLength = BitConverter.ToInt32(bytes, 0);
            grainState.ETag = Encoding.UTF8.GetString(bytes, sizeof(int), etagLength);
            grainState.State = _serializer.Deserialize<T>(new ArraySegment<byte>(
                bytes, sizeof(int) + etagLength, bytes.Length - sizeof(int) - etagLength));
            grainState.RecordExists = true;
        }
        else
        {
            grainState.ETag = null;
            grainState.RecordExists = false;
        }

    }

    public async Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        var etag = Guid.NewGuid().ToString("N");
        var etagBytes = Encoding.UTF8.GetBytes(etag);
        var stateBytes = _serializer.SerializeToArray(grainState.State);
        var bytes = new byte[sizeof(int) + etagBytes.Length + stateBytes.Length];
        BitConverter.GetBytes(etagBytes.Length).CopyTo(bytes, 0);
        etagBytes.CopyTo(bytes, sizeof(int));
        stateBytes.CopyTo(bytes, sizeof(int) + etagBytes.Length);
        var path = PathFor(stateName, grainId);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        grainState.ETag = etag;
        grainState.RecordExists = true;
    }

    public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState)
    {
        File.Delete(PathFor(stateName, grainId));
        grainState.ETag = null;
        grainState.RecordExists = false;
        return Task.CompletedTask;
    }

    private string PathFor(string stateName, GrainId grainId)
    {
        var key = Encoding.UTF8.GetBytes($"{stateName}\0{grainId}");
        return Path.Combine(_directory, $"{Convert.ToHexStringLower(SHA256.HashData(key))}.state");
    }
}
