using System.Text.Json;

namespace DigitalBrain.Abstractions.Descriptors;

// Lets callers construct arguments with the same JSON contract the kernel reads.
public sealed record ArgumentContract(JsonSerializerOptions Options, string? CommandIdPropertyName);
