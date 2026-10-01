using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Modules.Assistant.Tests.Unit;

// Only provider configuration is hidden from the real runner, selecting its injected
// deterministic IChatClient. All tool dispatch, context, and event handling stay real.
internal sealed class InjectedModelTurnRunner(IServiceProvider services) : IAgentTurnRunner
{
    public ConcurrentQueue<string> CompletedTools { get; } = new();
    public ConcurrentQueue<string> Failures { get; } = new();
    public async IAsyncEnumerable<AgentTurnEvent> RunAsync(AgentTurnRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var item in new AgentTurnRunner(new InjectedModelServices(services)).RunAsync(request with { Model = null }, ct))
        {
            if (item is AgentTurnEvent.Failed failure) { Failures.Enqueue(failure.Message); }
            if (item is AgentTurnEvent.ToolCompleted tool) { CompletedTools.Enqueue(tool.Name); }
            yield return item;
        }
    }
    private sealed class InjectedModelServices(IServiceProvider original) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(IOptions<AIOptions>) ? Options.Create(new AIOptions())
            : type == typeof(IAiCredentials) ? new FixedAiCredentials()
            : original.GetService(type);
    }
}
