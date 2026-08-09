using System.Text.Json;

namespace DigitalBrain.Poc.Acceptance.Tests;

internal sealed record ScenarioWireResponse(
    string Id,
    bool Success,
    JsonElement Payload,
    string? ErrorType,
    string? ErrorMessage);
