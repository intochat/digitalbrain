using Orleans.Storage;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>Options for one <see cref="FileGrainStorage"/> store.</summary>
public sealed class FileGrainStorageOptions
{
    /// <summary>The folder that holds the store's files.</summary>
    public string RootDirectory { get; set; } = string.Empty;

    /// <summary>The state serializer; null uses the silo's <see cref="IGrainStorageSerializer"/>.</summary>
    public IGrainStorageSerializer? GrainStorageSerializer { get; set; }

    /// <summary>The service id mixed into file names; null uses the cluster's service id.</summary>
    public string? ServiceId { get; set; }
}
