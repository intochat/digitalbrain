using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain.Runtime.Orleans.Signalling;

/// <summary>Source-generated metadata for the Orleans runtime's envelope types; registered with RuntimeProtocol by AddNeuronHosting.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(SessionBatch))]
[JsonSerializable(typeof(SessionSignal))]
internal sealed partial class RuntimeOrleansJsonContext : JsonSerializerContext;
