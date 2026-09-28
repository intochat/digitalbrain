using System.Runtime.CompilerServices;
using DigitalBrain.Contracts;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI.Metering;

/// <summary>
/// Records the provider's reported token classes for every chat call made under an
/// <see cref="IntentContext"/>. It sits at the innermost pipeline layer so it measures one raw
/// provider call, and it records once per call — streaming usage is accumulated from the
/// updates exactly as <see cref="ChatResponseExtensions.ToChatResponse(IEnumerable{ChatResponseUpdate})"/>
/// aggregates it, so a turn is never counted twice.
/// </summary>
internal sealed class MeteringChatClient(IChatClient innerClient, IIntentUsageSink sink, string provider, string model)
    : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        await RecordAsync(response.Usage, response.ModelId, options?.ModelId, cancellationToken).ConfigureAwait(false);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        UsageDetails? usage = null;
        string? reportedModel = options?.ModelId;
        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                if (update.ModelId is { Length: > 0 } updateModel) { reportedModel = updateModel; }
                foreach (var content in update.Contents)
                {
                    if (content is UsageContent { Details: { } details }) { (usage ??= new()).Add(details); }
                }
                yield return update;
            }
        }
        finally
        {
            // A stream abandoned before its final chunk still records an explicit unreported entry.
            await RecordAsync(usage, reportedModel, options?.ModelId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task RecordAsync(UsageDetails? usage, string? responseModel, string? optionModel, CancellationToken cancellationToken)
    {
        if (IntentContext.Current is not { } intent) { return; }
        var entry = new TokenUsageEntry(MeterKind.Chat, provider, responseModel ?? optionModel ?? model,
            usage?.InputTokenCount, usage?.CachedInputTokenCount, usage?.ReasoningTokenCount,
            usage?.OutputTokenCount, usage?.TotalTokenCount, usage is not null, DateTimeOffset.UtcNow);
        try { await sink.RecordAsync(intent.IntentId, entry, cancellationToken).ConfigureAwait(false); }
        catch { /* Metering is an observer: a storage fault must never fail the model call. */ }
    }
}