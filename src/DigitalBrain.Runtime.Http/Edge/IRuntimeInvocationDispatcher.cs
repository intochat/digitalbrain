using System.Text.Json;
using DigitalBrain.Invocation;

namespace DigitalBrain.Runtime.Http;

/// <summary>
/// Dispatches runtime invocations from the HTTP edge into the host runtime and policies.
/// </summary>
public interface IRuntimeInvocationDispatcher
{
    /// <summary>
    /// Invokes the specified operation in the name of the caller/behavior.
    /// </summary>
    Task<JsonElement?> DispatchAsync(string brainId, RuntimeInvocation invocation, CancellationToken cancellationToken);
}
