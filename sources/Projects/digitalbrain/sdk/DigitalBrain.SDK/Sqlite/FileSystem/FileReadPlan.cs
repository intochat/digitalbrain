using DigitalBrain.Runtime.Neurons;
using System.Security.Cryptography;

namespace DigitalBrain.SDK.Sqlite.FileSystem;

// Pure decision logic for FileReadNeuron: response shaping, sha256, base64 encoding.
// No Orleans/grain context — unit-testable without booting Aspire.
public static class FileReadPlan
{
    public const long ContentLimitBytes = 256 * 1024;

    public static ReadFileResponse BuildResponse(
        ReadFileRequest req,
        Guid callerNeuronId,
        string callerNeuronType,
        DateTimeOffset timestamp,
        long size,
        byte[]? bytes,
        string? error)
    {
        var sha = bytes is null ? "" : Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var b64 = req.IncludeContent && bytes is not null ? Convert.ToBase64String(bytes) : null;
        return new ReadFileResponse(FilePath:           req.FilePath,
        SizeBytes:          size,
        Sha256:             sha,
        ContentBase64:      b64,
        Error:              error) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: callerNeuronId,
            callerNeuronType: callerNeuronType,
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? "External",
            timestamp: timestamp
        ) };
    }

    public static BrowseFilesResponse BuildBrowseResponse(
        BrowseFilesRequest req,
        Guid callerNeuronId,
        string callerNeuronType,
        DateTimeOffset timestamp,
        IReadOnlyList<string> paths) =>
        new(Paths:              paths) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: req.CorrelationId,
            causationId: req.SynapseId,
            callerNeuronId: callerNeuronId,
            callerNeuronType: callerNeuronType,
            receiverNeuronId: req.CallerNeuronId,
            receiverNeuronType: req.CallerNeuronType ?? "External",
            timestamp: timestamp
        ) };
}
