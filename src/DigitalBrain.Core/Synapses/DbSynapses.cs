namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("DigitalBrain.Core.DbConnect")]
public record DbConnect(string ConnectionName, string Provider, string ConnectionString) : Synapse(nameof(DbConnect), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.DbQuery")]
public record DbQuery(string ConnectionName, string Query, string? Result = null) : Synapse(nameof(DbQuery), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.DbInspectSchema")]
public record DbInspectSchema(
    string ConnectionName,
    string Provider,
    string? ConnectionString = null,
    string? SourcePath = null,
    string? ClientId = null,
    string? WorkspaceId = null) : Synapse(nameof(DbInspectSchema), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.DbSchemaInspected")]
public record DbSchemaInspected(
    string ConnectionName,
    string Provider,
    DbSchemaModel? Schema,
    bool Succeeded = true,
    string? Error = null,
    string? ClientId = null,
    string? WorkspaceId = null) : Synapse(nameof(DbSchemaInspected), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.DbSchemaModel")]
public record DbSchemaModel(
    [property: Id(0)] string ConnectionName,
    [property: Id(1)] string Provider,
    [property: Id(2)] IReadOnlyList<DbTable> Tables,
    [property: Id(3)] string? SourcePath = null,
    [property: Id(4)] string? SessionId = null,
    [property: Id(5)] IReadOnlyDictionary<string, string?>? Metadata = null,
    [property: Id(6)] string? WorkspaceId = null);

[GenerateSerializer]
[Alias("DigitalBrain.Core.DbTable")]
public record DbTable(
    [property: Id(0)] string Name,
    [property: Id(1)] string Kind,
    [property: Id(2)] IReadOnlyList<DbColumn> Columns,
    [property: Id(3)] IReadOnlyList<DbForeignKey> ForeignKeys,
    [property: Id(4)] IReadOnlyList<DbIndex> Indexes,
    [property: Id(5)] string? Schema = null,
    [property: Id(6)] IReadOnlyDictionary<string, string?>? Metadata = null);

[GenerateSerializer]
[Alias("DigitalBrain.Core.DbColumn")]
public record DbColumn(
    [property: Id(0)] string Name,
    [property: Id(1)] string? StoreType,
    [property: Id(2)] bool IsNullable,
    [property: Id(3)] int PrimaryKeyOrdinal = 0,
    [property: Id(4)] string? DefaultValue = null,
    [property: Id(5)] int Ordinal = 0,
    [property: Id(6)] IReadOnlyDictionary<string, string?>? Metadata = null);

[GenerateSerializer]
[Alias("DigitalBrain.Core.DbForeignKey")]
public record DbForeignKey(
    [property: Id(0)] string Name,
    [property: Id(1)] string Table,
    [property: Id(2)] IReadOnlyList<string> Columns,
    [property: Id(3)] string PrincipalTable,
    [property: Id(4)] IReadOnlyList<string> PrincipalColumns,
    [property: Id(5)] string? OnUpdate = null,
    [property: Id(6)] string? OnDelete = null,
    [property: Id(7)] string? Match = null,
    [property: Id(8)] IReadOnlyDictionary<string, string?>? Metadata = null);

[GenerateSerializer]
[Alias("DigitalBrain.Core.DbIndex")]
public record DbIndex(
    [property: Id(0)] string Name,
    [property: Id(1)] string Table,
    [property: Id(2)] IReadOnlyList<string> Columns,
    [property: Id(3)] bool IsUnique = false,
    [property: Id(4)] bool IsPartial = false,
    [property: Id(5)] string? Origin = null,
    [property: Id(6)] IReadOnlyDictionary<string, string?>? Metadata = null);

[Alias("DigitalBrain.Core.IDbSupportNeuron")]
public interface IDbSupportNeuron : INeuron, IHandle<DbConnect>, IHandle<DbQuery>, IHandle<DbInspectSchema>
{
    const string SingletonKey = "db-main";
}
