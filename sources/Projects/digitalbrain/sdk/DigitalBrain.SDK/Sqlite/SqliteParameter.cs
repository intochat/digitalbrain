namespace DigitalBrain.SDK.Sqlite;

[GenerateSerializer]
public sealed record SqliteParameterValue(
    [property: Id(0)] string Name,
    [property: Id(1)] string? StringValue,
    [property: Id(2)] long? IntegerValue,
    [property: Id(3)] double? RealValue,
    [property: Id(4)] byte[]? BlobValue);
