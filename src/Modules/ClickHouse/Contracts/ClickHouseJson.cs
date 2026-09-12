using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.ClickHouse;

[assembly: NeuronJsonContext(typeof(ClickHouseJson))]

namespace DigitalBrain.ClickHouse;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ClickHouseQuery))]
[JsonSerializable(typeof(ClickHouseColumn))]
[JsonSerializable(typeof(ClickHouseQueryResult))]
[JsonSerializable(typeof(ReadClickHouseSchema))]
[JsonSerializable(typeof(ClickHouseTableInfo))]
[JsonSerializable(typeof(ClickHouseSchema))]
[JsonSerializable(typeof(ClickHouseConnection))]
[JsonSerializable(typeof(CreateQueryTable))]
[JsonSerializable(typeof(CreateQueryTableCommand))]
[JsonSerializable(typeof(ClickHouseTableState))]
[JsonSerializable(typeof(Accepted<string>))]
public sealed partial class ClickHouseJson : JsonSerializerContext;
