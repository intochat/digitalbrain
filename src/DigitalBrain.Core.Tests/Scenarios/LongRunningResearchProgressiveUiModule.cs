namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record ResearchJobRequested(string JobId, string Query) : Synapse;

public sealed record ResearchJobStarted(string JobId, string Query) : Synapse;

public sealed record ResearchJobProgress(string JobId, string Stage, int Percent) : Synapse;

public sealed record ResearchJobCompleted(string JobId, string Summary) : Synapse;

public sealed record PipelineSliceAsked(string JobId, string Query) : Synapse;

public sealed record PipelineSliceAnswered(string JobId, string Table) : Synapse;

public sealed record CallMentionsAsked(string JobId, string Query) : Synapse;

public sealed record CallMentionsAnswered(string JobId, string Mentions) : Synapse;

public sealed class ResearchJobState
{
    public string? JobId { get; set; }
    public string? Query { get; set; }
    public string? PipelineTable { get; set; }
    public string? Mentions { get; set; }
}

// Progressive research: Started → Progress (pipeline) → Progress (mentions) → Completed across turns.
public sealed class ResearchJobOrchestrator : Neuron<ResearchJobState>,
    INeuron<ResearchJobRequested>,
    INeuron<PipelineSliceAnswered>,
    INeuron<CallMentionsAnswered>
{
    public Task HandleAsync(ResearchJobRequested fact, CancellationToken cancellationToken)
    {
        State.JobId = fact.JobId;
        State.Query = fact.Query;
        State.PipelineTable = null;
        State.Mentions = null;
        Emit(new ResearchJobStarted(fact.JobId, fact.Query));
        Ask<PipelineSliceAnswered>(new PipelineSliceAsked(fact.JobId, fact.Query));
        return Task.CompletedTask;
    }

    public Task HandleAsync(PipelineSliceAnswered fact, CancellationToken cancellationToken)
    {
        if (!string.Equals(State.JobId, fact.JobId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        State.PipelineTable = fact.Table;
        Emit(new ResearchJobProgress(fact.JobId, Stage: "pipeline", Percent: 50));
        Ask<CallMentionsAnswered>(new CallMentionsAsked(fact.JobId, State.Query ?? string.Empty));
        return Task.CompletedTask;
    }

    public Task HandleAsync(CallMentionsAnswered fact, CancellationToken cancellationToken)
    {
        if (!string.Equals(State.JobId, fact.JobId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        State.Mentions = fact.Mentions;
        Emit(new ResearchJobProgress(fact.JobId, Stage: "mentions", Percent: 100));
        Emit(new ResearchJobCompleted(
            fact.JobId,
            Summary: $"{State.PipelineTable}; mentions={State.Mentions}"));
        return Task.CompletedTask;
    }
}

public sealed class CrmPipeline : Neuron, IAnswers<PipelineSliceAsked, PipelineSliceAnswered>
{
    public Task<PipelineSliceAnswered?> HandleAsync(
        PipelineSliceAsked question, CancellationToken cancellationToken)
        => Task.FromResult<PipelineSliceAnswered?>(
            new(question.JobId, Table: $"pipeline:{question.Query}"));
}

public sealed class TranscriptSearch : Neuron, IAnswers<CallMentionsAsked, CallMentionsAnswered>
{
    public Task<CallMentionsAnswered?> HandleAsync(
        CallMentionsAsked question, CancellationToken cancellationToken)
        => Task.FromResult<CallMentionsAnswered?>(
            new(question.JobId, Mentions: $"mentions:{question.Query}"));
}

// UI projector hears progressive research job facts (ordered journal is the proof).
public sealed class ResearchJobUiProjector : Neuron,
    INeuron<ResearchJobStarted>,
    INeuron<ResearchJobProgress>,
    INeuron<ResearchJobCompleted>
{
    public Task HandleAsync(ResearchJobStarted fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ResearchJobProgress fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ResearchJobCompleted fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
