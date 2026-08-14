namespace DigitalBrain.ProductHost.Persistence;

public enum ProductPersistenceKind
{
    PostgreSql,
    InMemory,
}

public enum ProductAuthorityKind
{
    External,
    LocalTest,
}

public enum ProductObjectStorageKind
{
    External,
    InMemory,
}

public enum ProductOrleansStorageKind
{
    Durable,
    InMemory,
}

public sealed class ProductStoreOptions
{
    public const string SectionName = "DigitalBrain:Product";

    public ProductPersistenceKind Persistence { get; set; } = ProductPersistenceKind.PostgreSql;

    public ProductAuthorityKind Authority { get; set; } = ProductAuthorityKind.External;

    public ProductObjectStorageKind ObjectStorage { get; set; } = ProductObjectStorageKind.External;

    public ProductOrleansStorageKind OrleansStorage { get; set; } = ProductOrleansStorageKind.Durable;

    public string? PostgreSqlConnectionString { get; set; }

    public string? ObjectStoreBucket { get; set; }

    public string? ObjectStoreEncryptionKeyId { get; set; }

    public string? OrleansClusteringConnectionString { get; set; }

    public string? OrleansGrainStorageConnectionString { get; set; }

    public string? OrleansReminderConnectionString { get; set; }
}
