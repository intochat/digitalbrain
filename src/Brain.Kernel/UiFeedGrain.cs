using Brain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Streams;

namespace Brain.Kernel;

[GrainType("ui-feed")]
public sealed class UiFeedGrain(
    [FromKeyedServices("ui-feed-frames")] IDurableList<UiFeedFrame> frames,
    [FromKeyedServices("ui-feed-events")] IDurableDictionary<string, byte> processedEvents,
    [FromKeyedServices("ui-feed-delivery")] IDurableDictionary<string, long> deliveryState)
    : DurableGrain, IUiFeed, IRemindable
{
    public const string LiveDeliveryReminderName = "ui-feed-live-retry";
    private const string PublishedSequenceKey = "published-sequence";

    private StreamSubscriptionHandle<EventSynapse<UiFeedCandidate>>? _candidateSubscription;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await EnsureSubscribedAsync();
        await DrainLiveAsync();
    }

    public async Task EnsureSubscribedAsync()
    {
        if (_candidateSubscription is not null)
            return;

        var (organization, space) = FeedIdentity();
        var stream = CandidateStream(organization, space);
        var handles = await stream.GetAllSubscriptionHandles();
        if (handles.Count > 0)
        {
            _candidateSubscription = await handles[0].ResumeAsync(OnCandidateAsync);
            return;
        }

        _candidateSubscription = await stream.SubscribeAsync(OnCandidateAsync);
    }

    public Task<UiFeedPage> ReadAsync(long cursor, int max)
    {
        if (cursor < 0)
            throw new ArgumentOutOfRangeException(nameof(cursor));

        var boundedMax = Math.Clamp(max, 1, 500);
        var pageFrames = frames
            .Where(frame => frame.Sequence > cursor)
            .Take(boundedMax)
            .ToArray();
        var nextCursor = pageFrames.Length == 0 ? cursor : pageFrames[^1].Sequence;
        return Task.FromResult(new UiFeedPage(pageFrames, nextCursor));
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!string.Equals(reminderName, LiveDeliveryReminderName, StringComparison.Ordinal))
            return;

        await DrainLiveAsync();
    }

    private async Task OnCandidateAsync(
        EventSynapse<UiFeedCandidate> candidate,
        StreamSequenceToken? token)
    {
        ValidateCandidate(candidate);
        var eventKey = candidate.Metadata.EventId.ToString("N");
        if (!processedEvents.ContainsKey(eventKey))
        {
            var sequence = frames.Count == 0 ? 1 : checked(frames[^1].Sequence + 1);
            frames.Add(ToFrame(sequence, candidate));
            processedEvents[eventKey] = 0;
            await WriteStateAsync(CancellationToken.None);
        }

        try
        {
            await DrainLiveAsync();
        }
        catch (Exception)
        {
            await RegisterLiveDeliveryReminderAsync();
            throw new BrainException(
                BrainErrors.FailureSanitized,
                ReactiveNeuronPipeline<UiFeedCandidate>.UnknownFailureMessage);
        }
    }

    private async Task DrainLiveAsync()
    {
        var publishedSequence = deliveryState.TryGetValue(PublishedSequenceKey, out var value)
            ? value
            : 0;
        if (publishedSequence >= (frames.Count == 0 ? 0 : frames[^1].Sequence))
        {
            await UnregisterLiveDeliveryReminderAsync();
            return;
        }

        var (organization, space) = FeedIdentity();
        var stream = this.GetStreamProvider(ReactiveNeuron<UiFeedCandidate>.DefaultStreamProviderName)
            .GetStream<UiFeedFrame>(StreamId.Create(
                UiFeedStreams.LiveNamespace,
                UiFeedStreams.StreamId(organization, space)));

        foreach (var frame in frames.Where(frame => frame.Sequence > publishedSequence))
        {
            await stream.OnNextAsync(frame);
            deliveryState[PublishedSequenceKey] = frame.Sequence;
            publishedSequence = frame.Sequence;
            await WriteStateAsync(CancellationToken.None);
        }

        await UnregisterLiveDeliveryReminderAsync();
    }

    private IAsyncStream<EventSynapse<UiFeedCandidate>> CandidateStream(
        OrganizationId organization,
        SpaceId space) =>
        this.GetStreamProvider(ReactiveNeuron<UiFeedCandidate>.DefaultStreamProviderName)
            .GetStream<EventSynapse<UiFeedCandidate>>(StreamId.Create(
                UiFeedStreams.CandidateNamespace,
                UiFeedStreams.StreamId(organization, space)));

    private (OrganizationId Organization, SpaceId Space) FeedIdentity()
    {
        var address = NeuronAddress.Parse(this.GetPrimaryKeyString());
        if (!string.Equals(address.ContractId, UiFeedStreams.ContractId, StringComparison.Ordinal)
            || !string.Equals(address.InstanceId, UiFeedStreams.InstanceId, StringComparison.Ordinal))
        {
            throw new BrainException(
                BrainErrors.FailureSanitized,
                ReactiveNeuronPipeline<UiFeedCandidate>.UnknownFailureMessage);
        }

        return (address.OrganizationId, address.SpaceId);
    }

    private void ValidateCandidate(EventSynapse<UiFeedCandidate> candidate)
    {
        var (organization, space) = FeedIdentity();
        var metadata = candidate.Metadata;
        if (metadata.EventId == Guid.Empty
            || metadata.OrganizationId != organization
            || metadata.SpaceId != space
            || metadata.Source.OrganizationId != organization
            || metadata.Source.SpaceId != space)
        {
            throw InvalidCandidate();
        }

        var payload = candidate.Payload;
        var valid = payload.Type switch
        {
            UiFeedFrameTypes.Snapshot =>
                payload.Snapshot is not null && payload.Patch is null && payload.FailureCode is null,
            UiFeedFrameTypes.Patch =>
                payload.Snapshot is null && payload.Patch is not null && payload.FailureCode is null,
            UiFeedFrameTypes.Failure =>
                payload.Snapshot is null && payload.Patch is null && IsFailureCode(payload.FailureCode),
            _ => false,
        };
        if (!valid)
            throw InvalidCandidate();
    }

    private static UiFeedFrame ToFrame(
        long sequence,
        EventSynapse<UiFeedCandidate> candidate) =>
        new(
            UiFeedFrame.CurrentSchemaVersion,
            sequence,
            candidate.Metadata.EventId,
            candidate.Payload.Type,
            candidate.Payload.Snapshot,
            candidate.Payload.Patch,
            candidate.Payload.FailureCode);

    private static bool IsFailureCode(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(character =>
            character is >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '.'
            or '-');

    private static BrainException InvalidCandidate() =>
        new(
            BrainErrors.FailureSanitized,
            ReactiveNeuronPipeline<UiFeedCandidate>.UnknownFailureMessage);

    private Task RegisterLiveDeliveryReminderAsync() =>
        this.RegisterOrUpdateReminder(
            LiveDeliveryReminderName,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(1));

    private async Task UnregisterLiveDeliveryReminderAsync()
    {
        var reminder = await this.GetReminder(LiveDeliveryReminderName);
        if (reminder is not null)
            await this.UnregisterReminder(reminder);
    }
}
