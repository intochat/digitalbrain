using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Supabase;

namespace DigitalBrain.Supabase;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(SupabaseQuery))]
[JsonSerializable(typeof(SupabaseColumn))]
[JsonSerializable(typeof(SupabaseQueryResult))]
[JsonSerializable(typeof(ReadSupabaseSchema))]
[JsonSerializable(typeof(SupabaseTableInfo))]
[JsonSerializable(typeof(SupabaseSchema))]
[JsonSerializable(typeof(SupabaseConnection))]
[JsonSerializable(typeof(CreateQueryTable))]
[JsonSerializable(typeof(CreateQueryTableCommand))]
[JsonSerializable(typeof(SupabaseTableState))]
[JsonSerializable(typeof(Accepted<string>))]
public sealed partial class SupabaseJson : JsonSerializerContext;
