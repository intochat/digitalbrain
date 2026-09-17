using System.Runtime.CompilerServices;

namespace DigitalBrain.AI;

/// <summary>Observes durable requests without holding a grain turn or cancelling work on disconnect.</summary>
public static class AgentConversation
{
    public static async Task<AgentResponse> GetResponse(this IAgent agent, AgentRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = await agent.Submit(request).WaitAsync(cancellationToken);
        return await agent.WaitForResponse(response.TaskId, cancellationToken);
    }

    /// <summary>Streams persisted status changes and the final answer, not model token deltas.</summary>
    public static async IAsyncEnumerable<AgentResponse> GetResponseStream(this IAgent agent, AgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = await agent.Submit(request).WaitAsync(cancellationToken);
        await foreach (var update in agent.GetResponseStream(response.TaskId, cancellationToken))
        {
            yield return update;
        }
    }

    /// <summary>Resumes observation of an existing request using its durable identity.</summary>
    public static async IAsyncEnumerable<AgentResponse> GetResponseStream(this IAgent agent, string requestId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        AgentResponse? previous = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await agent.GetResponse(requestId).WaitAsync(cancellationToken)
                ?? throw new InvalidOperationException($"Agent request '{requestId}' does not exist.");
            if (response != previous)
            {
                yield return response;
                previous = response;
            }
            if (response.Status is not ("Queued" or "Running")) { yield break; }
            await Task.Delay(250, cancellationToken);
        }
    }

    public static async Task<AgentResponse> WaitForResponse(this IAgent agent, string requestId,
        CancellationToken cancellationToken = default)
    {
        await foreach (var response in agent.GetResponseStream(requestId, cancellationToken))
        {
            if (response.Status is not ("Queued" or "Running")) { return response; }
        }
        throw new InvalidOperationException("The response stream ended without a terminal result.");
    }
}
