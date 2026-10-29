using System.Text.Json;

namespace DigitalBrain.Invocation;

/// <summary>
/// Where a typed proxy hands its calls: TestOS and the host runtime each implement it, so both see the same encoded invocation.
/// The sink stamps <see cref="RuntimeInvocation.Behavior"/>: caller provenance belongs to the runtime, never to the proxy.
/// </summary>
public interface IInvocationSink
{
    /// <summary>Delivers a call and returns its JSON result, or null when the method returns nothing.</summary>
    Task<JsonElement?> Invoke(NeuronId target, RuntimeInvocation call, CancellationToken cancellationToken);

    /// <summary>Delivers a one-way call; the returned task completes once the call is handed over, not when it ran.</summary>
    Task Post(NeuronId target, RuntimeInvocation call);

    /// <summary>Delivers a streaming call and yields its items as JSON.</summary>
    IAsyncEnumerable<JsonElement> Stream(NeuronId target, RuntimeInvocation call, CancellationToken cancellationToken);
}
