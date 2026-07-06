using DigitalBrain.Core;

namespace DigitalBrain.Context;

[Alias("DigitalBrain.Context.IContextNeuron")]
public interface IContextNeuron : INeuron, IHandle<ContextUpdate>
{
    [Alias("GetContextAsync")]
    Task<string> GetContextAsync(string contextName);

    // Semantic memory: store a memory (embedded) and recall the most relevant ones for a query.
    // Recall uses an in-grain hybrid (cosine + keyword) scorer; with a NoOp embedder it degrades to keyword.
    [Alias("RememberAsync")]
    Task RememberAsync(string text);
    [Alias("RecallAsync")]
    Task<string[]> RecallAsync(string query, int top = 5);
}
