using DigitalBrain.Memory.Qdrant;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Memory;

/// <summary>Public module settings. Credentials remain in the host's secret configuration.</summary>
public sealed class MemoryModuleOptions
{
    public const string SectionName = "DigitalBrain:Memory";

    public string? Provider { get; set; }
    public QdrantMemoryOptions Qdrant { get; set; } = new();
    public bool HostQdrant { get; set; }

    internal void ResolveConnection(IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(Qdrant.ConnectionName))
        {
            Qdrant.ConnectionName = QdrantVectorMemoryRegistration.DefaultConnectionName;
        }

        Qdrant.ConnectionString = configuration.GetConnectionString(Qdrant.ConnectionName);
    }
}

public sealed class QdrantMemoryOptions
{
    public string ConnectionName { get; set; } = QdrantVectorMemoryRegistration.DefaultConnectionName;
    public string? CollectionName { get; set; }
    public string? ConnectionString { get; internal set; }
}
