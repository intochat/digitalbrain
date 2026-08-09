using System.Text.Json;

namespace DigitalBrain.Poc.Acceptance.Tests;

internal sealed record ScenarioWireRequest(string Id, string Command, JsonElement Payload);
