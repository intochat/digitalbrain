using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using Orleans.Journaling;

namespace DigitalBrain.SDK.Sqlite.FileSystem;

[ImplicitStreamSubscription(FileReadNeuronType)]
internal sealed class FileReadNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IFileSystemGateway fs,
    IGrainFactory grains,
    TimeProvider time,
    ILogger<FileReadNeuron> log)
    : Neuron(incoming, outgoing, grains, log),
      INeuron, INeuronMetadata,
      IHandle<ReadFileRequest>, IHandle<BrowseFilesRequest>
{
    public const string FileReadNeuronType = nameof(FileReadNeuron);

    public static NeuronId         Id           => new("data/file-read");
    public static string           Icon         => "data";
    public static NeuronCapability Capabilities => NeuronCapability.Storage;

    protected override async Task HandleSynapseAsync(Synapse s)
    {
        switch (s)
        {
            case ReadFileRequest req:
                await FireSynapseAsync(await ReadAsync(req));
                break;
            case BrowseFilesRequest browse:
                await FireSynapseAsync(Browse(browse));
                break;
        }
    }

    async Task<ReadFileResponse> ReadAsync(ReadFileRequest req)
    {
        Counter("filereader.requests").Increment(1);

        if (!fs.TryNormalize(req.FilePath, out var fullPath))
            return FileReadPlan.BuildResponse(req, InstanceId, FileReadNeuronType,
                time.GetUtcNow(), size: -1, bytes: null, error: "path outside repo root");

        var (size, bytes) = await fs.ReadAsync(fullPath, FileReadPlan.ContentLimitBytes, default);
        if (size < 0)
            return FileReadPlan.BuildResponse(req, InstanceId, FileReadNeuronType,
                time.GetUtcNow(), size: -1, bytes: null, error: "file not found");

        if (size > 0)
            Counter("filereader.bytes").Increment(size);

        return FileReadPlan.BuildResponse(req, InstanceId, FileReadNeuronType,
            time.GetUtcNow(), size, bytes, error: null);
    }

    BrowseFilesResponse Browse(BrowseFilesRequest req)
    {
        Counter("filereader.requests").Increment(1);
        return FileReadPlan.BuildBrowseResponse(req, InstanceId, FileReadNeuronType,
            time.GetUtcNow(),
            fs.EnumerateRelative(req.GlobPattern, req.MaxCount));
    }
}
