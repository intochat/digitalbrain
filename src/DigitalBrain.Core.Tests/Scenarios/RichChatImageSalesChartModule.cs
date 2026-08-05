using System.Collections.Immutable;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record RichChatUserMessaged(string Text, string AttachmentBlobRef) : Synapse;

public sealed record ChatAttachmentAdded(
    string BlobRef,
    string MimeType,
    int Width,
    int Height) : Synapse;

public sealed record VisionExtractAsked(string BlobRef, string SchemaHint) : Synapse;

public sealed record VisionStageRow(string Stage, int CountHint);

public sealed record VisionExtractAnswered(
    string BlobRef,
    ImmutableArray<VisionStageRow> Stages,
    string RawText,
    double Confidence) : Synapse;

public sealed record OpportunityStageStatsAsked(ImmutableArray<string> StageLabels) : Synapse;

public sealed record OpportunityStageStat(string Stage, int Count, double Amount);

public sealed record OpportunityStageStatsAnswered(
    ImmutableArray<OpportunityStageStat> Stats) : Synapse;

public sealed record FunnelTableProduced(
    string ChartId,
    ImmutableArray<string> Rows) : Synapse;

public sealed record RichChatAssistantSaid(string Caption) : Synapse;

// Multimodal desk: image attachment → vision ask → SF stage stats ask → chart + table artifacts.
public sealed class RichChatDesk : Neuron,
    INeuron<RichChatUserMessaged>,
    INeuron<VisionExtractAnswered>,
    INeuron<OpportunityStageStatsAnswered>
{
    public Task HandleAsync(RichChatUserMessaged fact, CancellationToken cancellationToken)
    {
        Emit(new ChatAttachmentAdded(
            fact.AttachmentBlobRef,
            MimeType: "image/png",
            Width: 1280,
            Height: 720));
        Ask<VisionExtractAnswered>(new VisionExtractAsked(
            fact.AttachmentBlobRef,
            SchemaHint: "sales_stages"));
        return Task.CompletedTask;
    }

    public Task HandleAsync(VisionExtractAnswered fact, CancellationToken cancellationToken)
    {
        if (fact.Confidence < 0.5)
        {
            Emit(new RichChatAssistantSaid(
                Caption: "OCR confidence too low — name the pipeline stages to chart."));
            return Task.CompletedTask;
        }

        var labels = fact.Stages.Select(row => row.Stage).ToImmutableArray();
        Ask<OpportunityStageStatsAnswered>(new OpportunityStageStatsAsked(labels));
        return Task.CompletedTask;
    }

    public Task HandleAsync(OpportunityStageStatsAnswered fact, CancellationToken cancellationToken)
    {
        var series = fact.Stats.Select(stat => $"{stat.Stage}:{stat.Count}").ToImmutableArray();
        var rows = fact.Stats
            .Select(stat => $"{stat.Stage}|{stat.Count}|{stat.Amount:0}")
            .ToImmutableArray();

        Emit(new ChartSpec(
            ChartId: "sales-funnel",
            Title: "Sales funnel (whiteboard + SF)",
            Series: series));
        Emit(new FunnelTableProduced(ChartId: "sales-funnel", Rows: rows));
        Emit(new RichChatAssistantSaid(
            Caption: "Funnel chart merges whiteboard stages with live Salesforce counts."));
        return Task.CompletedTask;
    }
}

public sealed class MockVisionExtract : Neuron, IAnswers<VisionExtractAsked, VisionExtractAnswered>
{
    public Task<VisionExtractAnswered?> HandleAsync(
        VisionExtractAsked question, CancellationToken cancellationToken)
        => Task.FromResult<VisionExtractAnswered?>(new(
            question.BlobRef,
            Stages:
            [
                new VisionStageRow("Prospect", 12),
                new VisionStageRow("Qualified", 7),
                new VisionStageRow("ClosedWon", 3),
            ],
            RawText: "Prospect → Qualified → ClosedWon",
            Confidence: 0.91));
}

public sealed class MockOpportunityStageStats
    : Neuron, IAnswers<OpportunityStageStatsAsked, OpportunityStageStatsAnswered>
{
    public Task<OpportunityStageStatsAnswered?> HandleAsync(
        OpportunityStageStatsAsked question, CancellationToken cancellationToken)
    {
        var stats = question.StageLabels
            .Select((stage, index) => new OpportunityStageStat(
                stage,
                Count: 10 - (index * 3),
                Amount: 50_000d * (10 - (index * 3))))
            .ToImmutableArray();
        return Task.FromResult<OpportunityStageStatsAnswered?>(new(stats));
    }
}

// Catalog sinks for ambient attachment / chart / table / caption.
public sealed class RichChatShellLedger : Neuron,
    INeuron<ChatAttachmentAdded>,
    INeuron<ChartSpec>,
    INeuron<FunnelTableProduced>,
    INeuron<RichChatAssistantSaid>
{
    public Task HandleAsync(ChatAttachmentAdded fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ChartSpec fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(FunnelTableProduced fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(RichChatAssistantSaid fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
