using System.Text.Json;

namespace DigitalBrain.Abstractions.Descriptors;

public sealed record MethodDescriptor(
    string InterfaceAlias,
    string MethodAlias,
    bool IsReadOnly,
    JsonElement? ArgsSchema,
    JsonElement? ResultSchema,
    string? Summary);
