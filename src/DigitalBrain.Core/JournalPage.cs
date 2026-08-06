namespace DigitalBrain;

public sealed record JournalPage(
    IReadOnlyList<JournalRecord> Records,
    long ReadThroughPosition,
    long JournalEndPosition) : JournalRead;
