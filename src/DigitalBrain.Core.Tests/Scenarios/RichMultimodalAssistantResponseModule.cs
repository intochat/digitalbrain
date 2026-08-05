using System.Collections.Immutable;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record MultimodalUserAsked(string Text) : Synapse;

public sealed record AssistantText(string Text) : Synapse;

public sealed record ChartSpec(string ChartId, string Title, ImmutableArray<string> Series) : Synapse;

public sealed record ImageRef(string BlobRef, string MimeType, string Alt) : Synapse;

public sealed record ButtonOffer(string ActionId, string Label) : Synapse;

// One user turn fans four separate multimodal synapses (not a single mega-payload).
public sealed class MultimodalAssistant : Neuron, INeuron<MultimodalUserAsked>
{
    public Task HandleAsync(MultimodalUserAsked fact, CancellationToken cancellationToken)
    {
        Emit(new AssistantText(
            Text: $"Portfolio health: {fact.Text} — returns steady; rebalance optional."));
        Emit(new ChartSpec(
            ChartId: "portfolio-30d",
            Title: "30-day performance",
            Series: ["equity", "benchmark"]));
        Emit(new ImageRef(
            BlobRef: "blob://charts/portfolio-30d.png",
            MimeType: "image/png",
            Alt: "Portfolio sparkline"));
        Emit(new ButtonOffer(ActionId: "propose-rebalance", Label: "Rebalance proposal"));
        return Task.CompletedTask;
    }
}

// Shell ledger hears every multimodal block as its own fact type.
public sealed class ShellMultimodalLedger : Neuron,
    INeuron<AssistantText>,
    INeuron<ChartSpec>,
    INeuron<ImageRef>,
    INeuron<ButtonOffer>
{
    public Task HandleAsync(AssistantText fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ChartSpec fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ImageRef fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(ButtonOffer fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
