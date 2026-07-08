using DigitalBrain.Core;

namespace DigitalBrain.Ino.Context;

[Alias("DigitalBrain.Ino.Context.IContextNeuron")]
public interface IContextNeuron : INeuron, IHandle<ContextUpdate>
{
    const string SingletonKey = "context-main";

    [Alias("GetContextAsync")]
    Task<string> GetContextAsync(string contextName, CancellationToken cancellationToken = default);

    // Semantic memory: store a memory (embedded) and recall the most relevant ones for a query.
    // Recall uses an in-grain hybrid (cosine + keyword) scorer; with a NoOp embedder it degrades to keyword.
    [Alias("RememberAsync")]
    Task RememberAsync(string text, CancellationToken cancellationToken = default);
    [Alias("RecallAsync")]
    Task<string[]> RecallAsync(string query, int top = 5, CancellationToken cancellationToken = default);
}
