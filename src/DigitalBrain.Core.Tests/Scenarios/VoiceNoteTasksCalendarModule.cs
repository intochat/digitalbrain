namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record VoiceNoteReceived(
    string BlobRef,
    int DurationSeconds,
    string DeviceId) : Synapse;

// Shared ambient transcript shape for voice STT mock (not meeting transcript).
public sealed record VoiceTranscriptReady(
    string BlobRef,
    string Text,
    double Confidence) : Synapse;

public sealed record CalendarBlockProposed(
    string BlockId,
    string Title,
    string DurationHint,
    string SourceBlobRef) : Synapse;

// Mock STT: VoiceNoteReceived → VoiceTranscriptReady (no network / no audio decode).
public sealed class MockVoiceTranscriber : Neuron, INeuron<VoiceNoteReceived>
{
    public Task HandleAsync(VoiceNoteReceived fact, CancellationToken cancellationToken)
    {
        Emit(new VoiceTranscriptReady(
            fact.BlobRef,
            Text: "Need to call Priya about the contract Monday morning, send the redlines today, and block two hours Friday for the board deck.",
            Confidence: 0.94));
        return Task.CompletedTask;
    }
}

// Extraction: transcript → task materialization + calendar hold proposal (same Cause turn).
public sealed class VoiceActionExtractor : Neuron, INeuron<VoiceTranscriptReady>
{
    public Task HandleAsync(VoiceTranscriptReady fact, CancellationToken cancellationToken)
    {
        if (fact.Confidence < 0.5)
        {
            return Task.CompletedTask;
        }

        // Reuse S36 TaskCreated vocabulary (Tag=voice); TaskStore is the catalog sink.
        Emit(new TaskCreated(
            TaskId: $"voice-task-{fact.BlobRef}",
            Title: "Call Priya about the contract",
            SourceMessageId: fact.BlobRef,
            Tag: "voice"));
        Emit(new TaskCreated(
            TaskId: $"voice-task-redlines-{fact.BlobRef}",
            Title: "Send the redlines today",
            SourceMessageId: fact.BlobRef,
            Tag: "voice"));
        Emit(new CalendarBlockProposed(
            BlockId: $"block-board-{fact.BlobRef}",
            Title: "Board deck focus",
            DurationHint: "2h",
            SourceBlobRef: fact.BlobRef));
        return Task.CompletedTask;
    }
}

// Catalog sink for calendar proposal ambient emit.
public sealed class CalendarBlockLedger : Neuron, INeuron<CalendarBlockProposed>
{
    public Task HandleAsync(CalendarBlockProposed fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

// Catalog sink for VoiceTranscriptReady when extractor is the only domain consumer (Emit needs ≥1).
// Extractor declares INeuron so transcript is catalogued; this ledger is optional surface for proof.
public sealed class VoiceTranscriptLedger : Neuron, INeuron<VoiceTranscriptReady>
{
    public Task HandleAsync(VoiceTranscriptReady fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
