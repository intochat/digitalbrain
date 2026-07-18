using Core.Memory;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Recall.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;

namespace Ino.Domains.Recall.Neurons;

/// <summary>
/// Canonical handler for <see cref="RecallQuestion"/>. Pure-code grain — no
/// LLM, no journal — just wraps IAW's <see cref="IMemoryLookup"/> so the
/// recall neuron is a thin shim over the existing Qdrant collection.
///
/// Activated correlation-keyed by <see cref="IFirePort"/> dispatch (standard
/// canonical handler shape). Per-user state lives in Qdrant under the
/// <c>user-memory-{userId}</c> collection IAW already populates from its
/// own chat pipeline; the user id flows in via
/// <see cref="NeuronContext.UserId"/>.
/// </summary>
public sealed class RecallNeuron(
    IMemoryLookup memoryLookup,
    ILogger<RecallNeuron>? log = null) : Grain, INeuron<RecallQuestion>
{
    private readonly ILogger _log = (ILogger?)log ?? NullLogger.Instance;

    public async Task<NeuronResult> HandleAsync(
        RecallQuestion synapse,
        NeuronContext ctx,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        ArgumentNullException.ThrowIfNull(ctx);

        var userId = ctx.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            _log.LogInformation(
                "RecallNeuron: skipping lookup with no UserId in context (correlation {Correlation})",
                ctx.CorrelationId.Value);
            return NeuronResult.Ok(
                "I can only recall things you've told me — sign in so I know which conversation history to search.");
        }

        var hit = await memoryLookup.LookupOriginAsync(userId, synapse.Text, ct);
        if (hit is null)
        {
            _log.LogInformation(
                "RecallNeuron: no hit for user {User} on question {Question}",
                userId, synapse.Text);
            return NeuronResult.Ok("I don't recall anything matching that yet.");
        }

        var when = hit.CreatedAt.ToString("yyyy-MM-dd");
        return NeuronResult.Ok($"On {when} you said: {hit.Content}");
    }
}
