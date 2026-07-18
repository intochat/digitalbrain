using DigitalBrain.Core;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer.Directory;

[GrainType("DigitalBrain.Developer.DirectoryNeuron")]
internal sealed class DirectoryNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ILogger<DirectoryNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      IDirectoryNeuron,
      IResourceNeuronTarget,
      ICallNeuronTarget,
      IHandle<ReadDirectoryRequest>
{
    private string DirectoryPath => this.GetPrimaryKeyString();

    public Task<string> GetDirectoryPathAsync() => Task.FromResult(DirectoryPath);

    public async Task<string> AskAsync(string prompt)
    {
        if (string.Equals(prompt, "read", StringComparison.OrdinalIgnoreCase))
        {
            return await ReadAsync("", CancellationToken.None) ?? "";
        }
        if (prompt.StartsWith("read ", StringComparison.OrdinalIgnoreCase))
        {
            var key = prompt["read ".Length..];
            return await ReadAsync(key, CancellationToken.None) ?? "";
        }
        if (prompt.StartsWith("write ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = prompt["write ".Length..];
            var parts = rest.Split(' ', 2);
            if (parts.Length == 2)
            {
                await WriteAsync(parts[0], parts[1], CancellationToken.None);
                return "ok";
            }
        }
        return "unknown command";
    }

    public Task<IReadOnlyList<string>> GetFilesAsync()
    {
        if (!System.IO.Directory.Exists(DirectoryPath))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var files = System.IO.Directory.GetFiles(DirectoryPath);
        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    public Task<IReadOnlyList<string>> GetDirectoriesAsync()
    {
        if (!System.IO.Directory.Exists(DirectoryPath))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        var dirs = System.IO.Directory.GetDirectories(DirectoryPath);
        return Task.FromResult<IReadOnlyList<string>>(dirs);
    }

    public async Task<string?> ReadAsync(string key, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(key))
            {
                // Return listing of directory contents if key is empty
                var files = await GetFilesAsync();
                var dirs = await GetDirectoriesAsync();
                return System.Text.Json.JsonSerializer.Serialize(new { files, directories = dirs });
            }

            var targetFile = Path.Combine(DirectoryPath, key);
            if (!System.IO.File.Exists(targetFile)) return null;

            return await System.IO.File.ReadAllTextAsync(targetFile, ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task WriteAsync(string key, string value, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(key)) return;

        var targetFile = Path.Combine(DirectoryPath, key);
        var dir = Path.GetDirectoryName(targetFile);
        if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
        {
            System.IO.Directory.CreateDirectory(dir);
        }

        await System.IO.File.WriteAllTextAsync(targetFile, value, ct);
    }

    public async Task HandleAsync(ReadDirectoryRequest synapse, CancellationToken cancellationToken)
    {
        try
        {
            var files = await GetFilesAsync();
            var dirs = await GetDirectoriesAsync();
            await FireResponseAsync(synapse, success: true, files: files, directories: dirs);
        }
        catch (Exception ex)
        {
            await FireResponseAsync(synapse, success: false, files: Array.Empty<string>(), directories: Array.Empty<string>(), errorMessage: ex.Message);
        }
    }

    private async Task FireResponseAsync(ReadDirectoryRequest request, bool success, IReadOnlyList<string> files, IReadOnlyList<string> directories, string? errorMessage = null)
    {
        var responseHeaders = new SynapseMetadata(
            SynapseId: SynapseId.New(),
            CorrelationId: request.Headers.CorrelationId,
            CausationId: new CausationId(request.Headers.SynapseId.Value),
            CallerNeuronId: new NeuronId(InstanceId.ToString()),
            CallerNeuronType: NeuronType,
            ReceiverNeuronId: request.Headers.CallerNeuronId,
            ReceiverNeuronType: request.Headers.CallerNeuronType ?? "External",
            Timestamp: DateTimeOffset.UtcNow
        );

        var response = new DirectoryContentsResponse(Success: success,
            Files: files,
            Directories: directories,
            ErrorMessage: errorMessage) { Headers = responseHeaders };

        await FireSynapseAsync(response);
    }
}
