using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;
using Orleans.Journaling;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

[GrainType("DigitalBrain.SDK.Developer.FileStore")]
internal sealed class FileStore(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ILogger<FileStore> logger)
    : Neuron(incoming, outgoing, grains, logger),
      ICallNeuronTarget
{
    public async Task<string> AskAsync(string prompt)
    {
        Logger.LogInformation("FileStore handling prompt: '{Prompt}'", prompt);
        if (prompt.StartsWith("create-folder ", StringComparison.OrdinalIgnoreCase))
        {
            var path = prompt["create-folder ".Length..].Trim();
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(@"D:\", path);
            }
            try
            {
                var directoryNeuron = Grains.GetGrain<IDirectoryNeuron>(path);
                await directoryNeuron.WriteAsync("status.txt", "initialized", CancellationToken.None);
                Logger.LogInformation("FileStore successfully created directory and status.txt at '{Path}'", path);
                return "success:" + path.Replace("\\", "/");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "FileStore failed to create directory at '{Path}'", path);
                return "error:" + ex.Message;
            }
        }
        return "unknown command";
    }
}
