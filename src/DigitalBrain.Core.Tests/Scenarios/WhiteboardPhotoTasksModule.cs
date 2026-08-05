using System.Collections.Immutable;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record WhiteboardImageAttached(string BlobRef, string Mime) : Synapse;

public sealed record WhiteboardImageStored(string BlobRef, string Mime) : Synapse;

public sealed record RunOcrAsked(string BlobRef) : Synapse;

public sealed record OcrTextReady(string BlobRef, string Text, double Confidence) : Synapse;

public sealed record WhiteboardParsed(
    string BlobRef,
    ImmutableArray<string> CandidateTitles) : Synapse;

public sealed record WhiteboardTasksProposed(
    string BlobRef,
    ImmutableArray<string> Titles) : Synapse;

public sealed record WhiteboardConfirmTasks(string BlobRef) : Synapse;

public sealed record WhiteboardTaskCreated(
    string TaskId,
    string Title,
    string BlobRef) : Synapse;

// Ingress: store + ask OCR (worker stand-in).
public sealed class WhiteboardVisionIngress : Neuron,
    INeuron<WhiteboardImageAttached>,
    INeuron<OcrTextReady>
{
    public Task HandleAsync(WhiteboardImageAttached fact, CancellationToken cancellationToken)
    {
        Emit(new WhiteboardImageStored(fact.BlobRef, fact.Mime));
        Ask<OcrTextReady>(new RunOcrAsked(fact.BlobRef));
        return Task.CompletedTask;
    }

    public Task HandleAsync(OcrTextReady fact, CancellationToken cancellationToken)
    {
        var titles = fact.Text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 2)
            .Take(5)
            .ToImmutableArray();
        Emit(new WhiteboardParsed(fact.BlobRef, titles));
        Emit(new WhiteboardTasksProposed(fact.BlobRef, titles));
        return Task.CompletedTask;
    }
}

// Deterministic OCR answerer (stateless-worker stand-in inside neuron).
public sealed class WhiteboardOcrWorker : Neuron, IAnswers<RunOcrAsked, OcrTextReady>
{
    public Task<OcrTextReady?> HandleAsync(RunOcrAsked question, CancellationToken cancellationToken)
        => Task.FromResult<OcrTextReady?>(new OcrTextReady(
            question.BlobRef,
            Text: "Call Priya about contract\nSend redlines today\nBlock Friday for board deck",
            Confidence: 0.91));
}

// Confirm → materialize tasks from last proposed titles in TState.
public sealed class WhiteboardTaskStore : Neuron<WhiteboardTaskStoreState>,
    INeuron<WhiteboardTasksProposed>,
    INeuron<WhiteboardConfirmTasks>
{
    public Task HandleAsync(WhiteboardTasksProposed fact, CancellationToken cancellationToken)
    {
        State.PendingBlobRef = fact.BlobRef;
        State.PendingTitles = [.. fact.Titles];
        return Task.CompletedTask;
    }

    public Task HandleAsync(WhiteboardConfirmTasks fact, CancellationToken cancellationToken)
    {
        if (State.PendingBlobRef is null
            || !string.Equals(State.PendingBlobRef, fact.BlobRef, StringComparison.Ordinal)
            || State.PendingTitles.Count == 0)
        {
            return Task.CompletedTask;
        }

        var index = 0;
        foreach (var title in State.PendingTitles)
        {
            index++;
            Emit(new WhiteboardTaskCreated(
                TaskId: $"wb-{fact.BlobRef}-{index}",
                Title: title,
                BlobRef: fact.BlobRef));
        }

        State.PendingTitles = [];
        State.PendingBlobRef = null;
        return Task.CompletedTask;
    }
}

public sealed class WhiteboardTaskStoreState
{
    public string? PendingBlobRef { get; set; }
#pragma warning disable CA1002, CA2227
    public List<string> PendingTitles { get; set; } = [];
#pragma warning restore CA1002, CA2227
}

public sealed class WhiteboardUiLedger : Neuron,
    INeuron<WhiteboardImageStored>,
    INeuron<WhiteboardParsed>,
    INeuron<WhiteboardTasksProposed>,
    INeuron<WhiteboardTaskCreated>
{
    public Task HandleAsync(WhiteboardImageStored fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(WhiteboardParsed fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(WhiteboardTasksProposed fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(WhiteboardTaskCreated fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
