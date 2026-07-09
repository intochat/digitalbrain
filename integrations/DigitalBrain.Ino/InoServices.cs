using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using Orleans;

namespace DigitalBrain.Ino;

// Phase 3: extracted services for thin InoNeuron.
// In full, these would be registered in DI and injected.

public interface IInoRuntime
{
    Task<string> HandleGenericAsync(InoRequest request, string workspaceId, CancellationToken ct = default);
}

public interface IInoToolRegistry
{
    IReadOnlyList<AIFunction> GetTools(string? clientId, CancellationToken ct);
}

public interface IInoContextBuilder
{
    Task<string> BuildAsync(string prompt, string workspaceId, CancellationToken ct);
}

public interface IInoAgentRunner
{
    Task<string> RunAsync(IChatClient chat, List<ChatMessage> messages, ChatOptions options, IReadOnlyList<AIFunction> tools, CancellationToken ct);
}

public interface IInoSurfaceEmitter
{
    Task DeliverReplyAsync(string text, string? clientId, string workspaceId, CancellationToken ct);
}

public interface IBrainAwarenessService
{
    Task<string> GetAwarenessAsync(string query, CancellationToken ct);
}

public interface IConnectionStateService
{
    Task<bool> IsConnectedAsync(string provider, string? clientId, CancellationToken ct);
}

public interface ITrustAwareMemoryService
{
    Task CreateSummaryAsync(string? workspaceId, CancellationToken ct);
}

// Basic impls for "at once" execution (delegates to existing grain logic for compat).
internal sealed class BasicInoRuntime : IInoRuntime
{
    public Task<string> HandleGenericAsync(InoRequest request, string workspaceId, CancellationToken ct = default)
        => Task.FromResult("Handled via InoRuntime stub (logic remains in thin InoNeuron for compat during batch phases)."); // real impl moves code here in split.
}

// Stubs for other services (Phase 3/4).
internal sealed class BasicInoToolRegistry : IInoToolRegistry
{
    public IReadOnlyList<AIFunction> GetTools(string? clientId, CancellationToken ct) => [];
}

internal sealed class BasicBrainAwarenessService : IBrainAwarenessService
{
    public Task<string> GetAwarenessAsync(string query, CancellationToken ct) => Task.FromResult("DigitalBrain neurons and synapses available via awareness (stub).");
}

internal sealed class BasicConnectionState : IConnectionStateService
{
    public Task<bool> IsConnectedAsync(string provider, string? clientId, CancellationToken ct) => Task.FromResult(true);
}
