using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Twitter;

[assembly: NeuronJsonContext(typeof(TwitterJson))]

namespace DigitalBrain.Twitter;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Post))]
[JsonSerializable(typeof(Posted))]
[JsonSerializable(typeof(TwitterSnapshot))]
[JsonSerializable(typeof(Accepted<Posted>))]
public sealed partial class TwitterJson : JsonSerializerContext;
