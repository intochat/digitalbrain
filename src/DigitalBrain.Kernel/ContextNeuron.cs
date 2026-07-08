using DigitalBrain.Core;
using DigitalBrain.Ino.Context;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Kernel;

[GrainType("context.manager.v1")]
public class ContextNeuron(ILogger<ContextNeuron> logger, NeuronJournals journals) : Neuron(logger, journals), IContextNeuron, IHandle<Signal>
{
    public async Task HandleAsync(ContextUpdate cmd, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Context updated: {Name}.{Key} = {Val}", cmd.ContextName, cmd.Key, cmd.Value);
        await FireAsync(cmd, cancellationToken);
    }

    public async Task HandleAsync(Signal signal, CancellationToken cancellationToken = default)
    {
        if (signal.Name != ContextSignals.RecallRequested)
        {
            return;
        }

        var query = signal.Props.TryGetValue("query", out var q) ? q?.ToString() ?? "" : "";
        var results = await RecallAsync(query, cancellationToken: cancellationToken);
        var replyProps = new Dictionary<string, object?>(signal.Props) { ["results"] = results };
        replyProps.Remove("query");
        await FireAsync(new Signal(ContextSignals.RecallCompleted, replyProps), cancellationToken);
    }

    public Task<string> GetContextAsync(string contextName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = OutgoingJournal.Concat(IncomingJournal).OfType<ContextUpdate>()
            .Where(c => c.ContextName == contextName)
            .Take(10)
            .Select(c => $"{c.Key}={c.Value}");
        return Task.FromResult(string.Join("; ", entries));
    }

    public async Task RememberAsync(string text, CancellationToken cancellationToken = default)
    {
        var embedding = await EmbedAsync(text, cancellationToken);
        await FireAsync(new MemoryStored(text, embedding), cancellationToken);
    }

    public async Task<string[]> RecallAsync(string query, int top = 5, CancellationToken cancellationToken = default)
    {
        var queryEmbedding = await EmbedAsync(query, cancellationToken);
        var memories = OutgoingJournal.Concat(IncomingJournal).OfType<MemoryStored>();
        return memories
            .Select(m => (m.Text, Score: HybridScorer.Score(query, m.Text, queryEmbedding, m.Embedding)))
            .Where(x => x.Score > 0f)
            .OrderByDescending(x => x.Score)
            .Take(top)
            .Select(x => x.Text)
            .ToArray();
    }

    private async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var generator = ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        if (generator is null)
        {
            return [];
        }

        try
        {
            var generated = await generator.GenerateAsync([text], cancellationToken: cancellationToken);
            return generated.First().Vector.ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Logger.LogWarning(ex, "Embedding generation failed; falling back to keyword-only context recall.");
            return [];
        }
    }
}


