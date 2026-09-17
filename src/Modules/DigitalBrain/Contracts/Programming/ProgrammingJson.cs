using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Programming;

[assembly: NeuronJsonContext(typeof(ProgrammingJson))]

namespace DigitalBrain.Abstractions.Programming;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProgramDefinition))]
[JsonSerializable(typeof(ProgramSnapshot))]
[JsonSerializable(typeof(ProgramRunSnapshot))]
[JsonSerializable(typeof(ProgramValidation))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
public sealed partial class ProgrammingJson : JsonSerializerContext;
