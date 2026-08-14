namespace Brain.Core.Outbox;

internal interface IOutboxStore
{
    IReadOnlyList<OutboxEntry> Emissions { get; }

    IReadOnlyList<DirectedMessage> DirectedMessages { get; }
}
