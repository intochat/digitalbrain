using DigitalBrain.Core;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer.File;

[GrainType("DigitalBrain.Developer.FileNeuron")]
internal sealed class FileNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ILogger<FileNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      IFileNeuron,
      IResourceNeuronTarget,
      ICallNeuronTarget,
      IHandle<ReadFileRequest>,
      IHandle<ApplyCodeEditRequest>
{
    private string FilePath => this.GetPrimaryKeyString();

    public Task<string> GetFilePathAsync() => Task.FromResult(FilePath);

    public async Task<string> AskAsync(string prompt)
    {
        if (string.Equals(prompt, "read", StringComparison.OrdinalIgnoreCase))
        {
            return await GetContentAsync();
        }
        if (prompt.StartsWith("write ", StringComparison.OrdinalIgnoreCase))
        {
            var val = prompt["write ".Length..];
            await ApplyEditAsync(val);
            return "ok";
        }
        return "unknown command";
    }

    public async Task<string> GetContentAsync()
    {
        if (!System.IO.File.Exists(FilePath))
            throw new FileNotFoundException("File not found.", FilePath);

        return await System.IO.File.ReadAllTextAsync(FilePath);
    }

    public async Task<bool> ApplyEditAsync(string newContent, string? commitMessage = null)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }
            await System.IO.File.WriteAllTextAsync(FilePath, newContent);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to apply edit to file {FilePath}", FilePath);
            return false;
        }
    }

    public async Task<string?> ReadAsync(string key, CancellationToken ct)
    {
        try
        {
            if (!System.IO.File.Exists(FilePath)) return null;
            return await System.IO.File.ReadAllTextAsync(FilePath, ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task WriteAsync(string key, string value, CancellationToken ct)
    {
        await ApplyEditAsync(value);
    }

    public async Task HandleAsync(ReadFileRequest synapse, CancellationToken cancellationToken)
    {
        try
        {
            var content = await GetContentAsync();
            await FireResponseAsync(synapse, success: true, content: content);
        }
        catch (Exception ex)
        {
            await FireResponseAsync(synapse, success: false, content: "", errorMessage: ex.Message);
        }
    }

    public async Task HandleAsync(ApplyCodeEditRequest synapse, CancellationToken cancellationToken)
    {
        var success = await ApplyEditAsync(synapse.NewContent, synapse.CommitMessage);
        var responseHeaders = new SynapseMetadata(
            SynapseId: SynapseId.New(),
            CorrelationId: synapse.Headers.CorrelationId,
            CausationId: new CausationId(synapse.Headers.SynapseId.Value),
            CallerNeuronId: new NeuronId(InstanceId.ToString()),
            CallerNeuronType: NeuronType,
            ReceiverNeuronId: synapse.Headers.CallerNeuronId,
            ReceiverNeuronType: synapse.Headers.CallerNeuronType ?? "External",
            Timestamp: DateTimeOffset.UtcNow
        );

        var response = new ApplyCodeEditResponse(Success: success,
            ErrorMessage: success ? null : $"Failed to write content to {FilePath}") { Headers = responseHeaders };

        await FireSynapseAsync(response, cancellationToken);
    }

    private async Task FireResponseAsync(ReadFileRequest request, bool success, string content, string? errorMessage = null)
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

        var response = new FileContentResponse(Success: success,
            Content: content,
            ErrorMessage: errorMessage) { Headers = responseHeaders };

        await FireSynapseAsync(response);
    }
}
