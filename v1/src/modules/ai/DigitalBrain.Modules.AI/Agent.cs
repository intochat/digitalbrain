using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.AI;

public abstract class Agent : Neuron, IAgent
{
    private readonly IChatClient _toolCallingClient;

    protected Agent(IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);

        _toolCallingClient = new ChatClientBuilder(chatClient)
            .UseFunctionInvocation()
            .Build();
    }

    protected virtual string? Instructions => null;

    protected virtual IReadOnlyList<AIFunction> AdditionalToolsFor(IReadOnlyList<ChatMessage> messages)
        => [];

    protected static AIFunction Capability(string name, string description, Delegate invoke)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(invoke);

        return AIFunctionFactory.Create(invoke, name, description);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        var turnScheduler = TaskScheduler.Current;
        var tools = await ResolveToolsAsync(messages, cancellationToken);
        var options = new ChatOptions
        {
            Tools = [.. tools.Select(tool => new TurnBoundFunction(tool, turnScheduler))],
            ToolMode = ChatToolMode.Auto,
        };
        var instructions = Instructions;
        IReadOnlyList<ChatMessage> request = string.IsNullOrWhiteSpace(instructions)
            ? messages
            : [new ChatMessage(ChatRole.System, instructions), .. messages];

        var selected = new List<string>();

        await foreach (var update in _toolCallingClient
            .GetStreamingResponseAsync(request, options, cancellationToken))
        {
            selected.AddRange(update.Contents.OfType<FunctionCallContent>().Select(call => call.Name));
            yield return update;
        }

        foreach (var capability in selected)
        {
            await EmitAsync(new CapabilityToolSelected(capability));
        }
    }

    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return RespondStreaming(messages).ToChatResponseAsync();
    }

    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return RespondStreaming(messages, cancellationToken).ToChatResponseAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<AIFunction>> ResolveToolsAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var additional = AdditionalToolsFor(messages);
        var discovered = await DiscoverCatalogToolsAsync(messages, cancellationToken);

        if (additional.Count == 0)
        {
            return discovered;
        }

        if (discovered.Count == 0)
        {
            return additional;
        }

        var merged = new List<AIFunction>(additional.Count + discovered.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in additional.Concat(discovered))
        {
            if (seen.Add(tool.Name))
            {
                merged.Add(tool);
            }
        }

        return merged;
    }

    private async Task<IReadOnlyList<AIFunction>> DiscoverCatalogToolsAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var catalog = ServiceProvider.GetService<ActiveCapabilityCatalog>();
        if (catalog is null)
        {
            return [];
        }

        var typeMap = ServiceProvider.GetService<ActiveModuleContractTypeMap>();
        var search = ServiceProvider.GetService<ICapabilityCandidateSearch>()
            ?? new VectorMemoryCapabilitySearch(GrainFactory);
        var router = new CapabilityRouter(
            catalog,
            search,
            ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger<CapabilityRouter>());
        var prompt = LatestOwnerText(messages);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return [];
        }

        var selected = await router.SelectAsync(Id.Owner, prompt, cancellationToken);
        if (selected.Count == 0 || typeMap is null)
        {
            return [];
        }

        var tools = new List<AIFunction>(selected.Count);
        foreach (var capability in selected)
        {
            try
            {
                tools.Add(SynapseCapabilityTool.Materialize(capability, GrainFactory, Id.Owner, typeMap));
            }
            catch (InvalidOperationException)
            {
                // Catalog may contain accepted synapses whose CLR types are not loadable in this process.
            }
        }

        return tools;
    }

    private static string LatestOwnerText(IReadOnlyList<ChatMessage> messages)
    {
        for (var turn = messages.Count - 1; turn >= 0; turn--)
        {
            if (messages[turn].Role == ChatRole.User)
            {
                return messages[turn].Text;
            }
        }

        return string.Empty;
    }
}
