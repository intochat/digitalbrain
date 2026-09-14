using System.Text.Json;
using DigitalBrain.Microsoft;

namespace DigitalBrain.Coding;

// The Coding module consumes the Microsoft module's capability but does not depend on it being composed:
// a silo without it refuses a promotion with advice instead of failing to resolve a service.
internal sealed class UnconfiguredAspireResourceCommands : IAspireResourceCommands
{
    public Task<JsonElement> ExecuteAsync(string resourceName, string command, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(
            $"Aspire is not configured in this silo, so resource '{resourceName}' cannot be sent '{command}'. Compose the Microsoft module with its AppHost project path.");
}
