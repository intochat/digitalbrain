using System.Text.Json;

namespace DigitalBrain.Microsoft;

// Starting and stopping a resource is a capability the Microsoft module owns; the Coding module's slot
// neuron consumes it through this seam so a fact can promote a slot with no Aspire process in sight.
public interface IAspireResourceCommands
{
    Task<JsonElement> ExecuteAsync(string resourceName, string command, CancellationToken cancellationToken = default);
}
