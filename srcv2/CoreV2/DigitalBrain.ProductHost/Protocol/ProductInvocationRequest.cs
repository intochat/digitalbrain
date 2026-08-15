using System.Text.Json;

namespace DigitalBrain.ProductHost.Protocol;

public sealed record ProductInvocationRequest(JsonElement Input);
