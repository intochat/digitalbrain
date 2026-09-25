using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Qdrant;

/// <summary>Public module settings. Credentials remain in the host's secret configuration.</summary>
public sealed class QdrantModuleOptions
{
    public const string SectionName = "DigitalBrain:Qdrant";

    public string? Provider { get; set; }
    public string ConnectionName { get; set; } = QdrantRegistration.DefaultConnectionName;
    public string CollectionName { get; set; } = QdrantNames.CollectionName;
    public int VectorSize { get; set; } = QdrantNames.DefaultVectorSize;
    public string? ConnectionString { get; internal set; }
    public QdrantResourceOptions Hosting { get; set; } = new();

    internal void ResolveConnection(IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(ConnectionName))
        {
            ConnectionName = QdrantRegistration.DefaultConnectionName;
        }

        if (string.IsNullOrWhiteSpace(CollectionName))
        {
            CollectionName = QdrantNames.CollectionName;
        }

        ConnectionString = configuration.GetConnectionString(ConnectionName);
    }
}

public sealed class QdrantResourceOptions
{
    public bool Enabled { get; set; }
    public bool PersistentStorage { get; set; } = true;
}
