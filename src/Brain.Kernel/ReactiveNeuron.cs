using Brain.Contracts;
using Orleans.Journaling;
using Orleans.Runtime;
using Orleans.Streams;

namespace Brain.Kernel;

public abstract class ReactiveNeuron<TOutboxEvent> : DurableGrain, IRemindable
{
    public const string OutboxReminderName = "reactive-outbox-retry";
    public const string DefaultStreamProviderName = "ReactiveStreamProvider";
    public const int DefaultMaxCausalDepth = 8;

    private readonly IDurableDictionary<string, CommandReceipt> _receipts;
    private readonly IDurableList<OutboxIntent<TOutboxEvent>> _outbox;
    private readonly IDurableList<SanitizedFailure> _failures;
    private readonly DurableReactiveStore _store;
    private readonly ReactiveNeuronPipeline<TOutboxEvent> _pipeline;
    private readonly List<object> _subscriptions = [];

    protected ReactiveNeuron(
        IDurableDictionary<string, CommandReceipt> receipts,
        IDurableDictionary<string, byte> processedEvents,
        IDurableDictionary<string, long> sourceSequences,
        IDurableList<OutboxIntent<TOutboxEvent>> outbox,
        IDurableDictionary<string, string> domain,
        IDurableDictionary<string, string> flags,
        IDurableList<SanitizedFailure> failures,
        IDurableDictionary<string, byte> acceptedCausation,
        IDurableDictionary<string, byte> rejectedCausation,
        int maxCausalDepth = DefaultMaxCausalDepth)
    {
        _receipts = receipts;
        _outbox = outbox;
        _failures = failures;
        _store = new DurableReactiveStore(
            this,
            receipts,
            processedEvents,
            sourceSequences,
            outbox,
            domain,
            flags,
            failures,
            acceptedCausation,
            rejectedCausation);
        _pipeline = new ReactiveNeuronPipeline<TOutboxEvent>(_store, maxCausalDepth);
    }

    protected bool FailNextCommit
    {
        get => _store.FailNextCommit;
        set => _store.FailNextCommit = value;
    }

    protected IDurableDictionary<string, CommandReceipt> Receipts => _receipts;
    protected IDurableList<OutboxIntent<TOutboxEvent>> Outbox => _outbox;
    protected IList<SanitizedFailure> Failures => _store.Failures;
    protected IDictionary<string, string> Flags => _store.Flags;
    protected long CurrentRevision => _pipeline.CurrentRevision;
    protected long UiRevision => _pipeline.UiRevision;
    protected string DomainState => _pipeline.DomainState;
    protected int ReactionCount => _pipeline.ReactionCount;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await OnReactiveActivateAsync(cancellationToken);
        if (AutoDrainAfterCommit)
            await DrainOutboxCoreAsync(throwOnPublishFailure: false);
    }

    protected virtual Task OnReactiveActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected virtual bool AutoDrainAfterCommit =>
        !_store.Flags.TryGetValue("auto-drain", out var value) || value != "0";

    protected async Task<CommandReceipt> ExecuteCommandCoreAsync<TCommand>(
        CommandSynapse<TCommand> command,
        CommandHandlerAsync<TCommand, TOutboxEvent> handler)
    {
        var receipt = await _pipeline.ExecuteCommandAsync(command, handler);
        if (AutoDrainAfterCommit)
            await DrainOutboxCoreAsync(throwOnPublishFailure: false);
        return receipt;
    }

    protected async Task HandleEventCoreAsync<TEvent>(
        EventSynapse<TEvent> @event,
        EventHandlerAsync<TEvent, TOutboxEvent> handler)
    {
        await _pipeline.HandleEventAsync(@event, handler);
        if (AutoDrainAfterCommit)
            await DrainOutboxCoreAsync(throwOnPublishFailure: false);
    }

    protected void EnsureExpectedUiRevision(long expectedRevision) =>
        _pipeline.EnsureExpectedUiRevision(expectedRevision);

    protected async Task RegisterEventSubscriptionAsync(
        string streamProviderName,
        string streamNamespace,
        Guid streamId,
        Func<EventSynapse<TOutboxEvent>, StreamSequenceToken?, Task> onEvent)
    {
        var provider = this.GetStreamProvider(streamProviderName);
        var stream = provider.GetStream<EventSynapse<TOutboxEvent>>(StreamId.Create(streamNamespace, streamId));
        var handles = await stream.GetAllSubscriptionHandles();
        if (handles.Count > 0)
        {
            foreach (var handle in handles)
                _subscriptions.Add(await handle.ResumeAsync((item, token) => onEvent(item, token)));
            return;
        }

        _subscriptions.Add(await stream.SubscribeAsync((item, token) => onEvent(item, token)));
    }

    protected int ActiveSubscriptionCount => _subscriptions.Count;

    protected void TrackSubscription(object subscriptionHandle) => _subscriptions.Add(subscriptionHandle);

    protected async Task PublishEventAsync(
        EventSynapse<TOutboxEvent> @event,
        string streamProviderName,
        string streamNamespace,
        Guid streamId)
    {
        var provider = this.GetStreamProvider(streamProviderName);
        var stream = provider.GetStream<EventSynapse<TOutboxEvent>>(StreamId.Create(streamNamespace, streamId));
        await stream.OnNextAsync(@event);
    }

    protected async Task DrainOutboxCoreAsync(bool throwOnPublishFailure = true)
    {
        if (_outbox.Count == 0)
        {
            await UnregisterOutboxReminderAsync();
            return;
        }

        while (_outbox.Count > 0)
        {
            var intent = _outbox[0];
            try
            {
                await PublishOutboxIntentAsync(intent);
                _outbox.RemoveAt(0);
            }
            catch (Exception)
            {
                _outbox[0] = intent.WithAttempt(intent.AttemptCount + 1);
                _failures.Add(new SanitizedFailure(
                    Guid.NewGuid(),
                    BrainErrors.FailureSanitized,
                    ReactiveNeuronPipeline<TOutboxEvent>.UnknownFailureMessage,
                    DateTimeOffset.UtcNow,
                    intent.Event.Metadata.CommandId,
                    intent.Event.Metadata.EventId));
                await WriteStateAsync();
                await RegisterOutboxReminderAsync();
                if (throwOnPublishFailure)
                    throw new BrainException(BrainErrors.FailureSanitized, ReactiveNeuronPipeline<TOutboxEvent>.UnknownFailureMessage);
                return;
            }
        }

        await WriteStateAsync();
        await UnregisterOutboxReminderAsync();
    }

    protected virtual Task PublishOutboxIntentAsync(OutboxIntent<TOutboxEvent> intent) =>
        PublishEventAsync(intent.Event, DefaultStreamProviderName, intent.StreamNamespace, intent.StreamId);

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != OutboxReminderName)
            return;

        await DrainOutboxCoreAsync(throwOnPublishFailure: false);
    }

    protected Task RegisterOutboxReminderAsync() =>
        this.RegisterOrUpdateReminder(
            OutboxReminderName,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(1));

    protected async Task UnregisterOutboxReminderAsync()
    {
        var reminder = await this.GetReminder(OutboxReminderName);
        if (reminder is not null)
            await this.UnregisterReminder(reminder);
    }

    protected Task<IGrainReminder?> GetOutboxReminderAsync() => this.GetReminder(OutboxReminderName);

    private sealed class DurableReactiveStore(
        ReactiveNeuron<TOutboxEvent> grain,
        IDurableDictionary<string, CommandReceipt> receipts,
        IDurableDictionary<string, byte> processedEvents,
        IDurableDictionary<string, long> sourceSequences,
        IDurableList<OutboxIntent<TOutboxEvent>> outbox,
        IDurableDictionary<string, string> domain,
        IDurableDictionary<string, string> flags,
        IDurableList<SanitizedFailure> failures,
        IDurableDictionary<string, byte> acceptedCausation,
        IDurableDictionary<string, byte> rejectedCausation) : IReactiveStore<TOutboxEvent>
    {
        public bool FailNextCommit { get; set; }

        public IDictionary<string, CommandReceipt> Receipts { get; } = new DurableDictionaryAdapter<CommandReceipt>(receipts);
        public IDictionary<string, byte> ProcessedEvents { get; } = new DurableDictionaryAdapter<byte>(processedEvents);
        public IDictionary<string, long> SourceSequences { get; } = new DurableDictionaryAdapter<long>(sourceSequences);
        public IList<OutboxIntent<TOutboxEvent>> Outbox { get; } = new DurableListAdapter<OutboxIntent<TOutboxEvent>>(outbox);
        public IDictionary<string, string> Domain { get; } = new DurableDictionaryAdapter<string>(domain);
        public IDictionary<string, string> Flags { get; } = new DurableDictionaryAdapter<string>(flags);
        public IList<SanitizedFailure> Failures { get; } = new DurableListAdapter<SanitizedFailure>(failures);
        public IDictionary<string, byte> AcceptedCausation { get; } = new DurableDictionaryAdapter<byte>(acceptedCausation);
        public IDictionary<string, byte> RejectedCausation { get; } = new DurableDictionaryAdapter<byte>(rejectedCausation);

        public async Task CommitAsync()
        {
            if (FailNextCommit)
            {
                FailNextCommit = false;
                throw new BrainException(BrainErrors.JournalCommitFailed, "journal write failed");
            }

            await grain.WriteStateAsync();
        }
    }

    private sealed class DurableDictionaryAdapter<TValue>(IDurableDictionary<string, TValue> inner) : IDictionary<string, TValue>
    {
        public TValue this[string key]
        {
            get => inner[key];
            set => inner[key] = value;
        }

        public ICollection<string> Keys => inner.Keys.ToArray();
        public ICollection<TValue> Values => inner.Values.ToArray();
        public int Count => inner.Count;
        public bool IsReadOnly => false;
        public void Add(string key, TValue value) => inner.Add(key, value);
        public void Add(KeyValuePair<string, TValue> item) => inner.Add(item.Key, item.Value);
        public void Clear() => inner.Clear();
        public bool Contains(KeyValuePair<string, TValue> item) => inner.ContainsKey(item.Key) && Equals(inner[item.Key], item.Value);
        public bool ContainsKey(string key) => inner.ContainsKey(key);
        public void CopyTo(KeyValuePair<string, TValue>[] array, int arrayIndex)
        {
            foreach (var pair in inner)
                array[arrayIndex++] = pair;
        }
        public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator() => inner.GetEnumerator();
        public bool Remove(string key) => inner.Remove(key);
        public bool Remove(KeyValuePair<string, TValue> item) => Contains(item) && inner.Remove(item.Key);
        public bool TryGetValue(string key, out TValue value) => inner.TryGetValue(key, out value!);
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class DurableListAdapter<T>(IDurableList<T> inner) : IList<T>
    {
        public T this[int index]
        {
            get => inner[index];
            set => inner[index] = value;
        }

        public int Count => inner.Count;
        public bool IsReadOnly => false;
        public void Add(T item) => inner.Add(item);
        public void Clear() => inner.Clear();
        public bool Contains(T item) => inner.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => inner.CopyTo(array, arrayIndex);
        public IEnumerator<T> GetEnumerator() => inner.GetEnumerator();
        public int IndexOf(T item) => inner.IndexOf(item);
        public void Insert(int index, T item) => inner.Insert(index, item);
        public bool Remove(T item) => inner.Remove(item);
        public void RemoveAt(int index) => inner.RemoveAt(index);
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
