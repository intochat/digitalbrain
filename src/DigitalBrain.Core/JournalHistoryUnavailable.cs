namespace DigitalBrain;

public sealed record JournalHistoryUnavailable(
    long RequestedAfterPosition,
    long AvailableFromPosition,
    long JournalEndPosition) : JournalRead;
