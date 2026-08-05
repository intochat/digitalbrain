using System.Collections.Immutable;

namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record McpToolInvokeRequested(
    string Tool,
    string ArgsHash,
    string ClientId,
    string OwnerBound,
    bool Mutating) : Synapse;

public sealed record McpToolInvoked(
    string Tool,
    string ArgsHash,
    string ClientId) : Synapse;

public sealed record ActiveNeuronsAsked(string OwnerBound) : Synapse;

public sealed record ActiveNeuronRow(string Kind, string Name);

public sealed record ActiveNeuronsAnswered(
    string OwnerBound,
    ImmutableArray<ActiveNeuronRow> Neurons) : Synapse;

public sealed record McpToolCompleted(
    string Tool,
    string ClientId,
    bool Ok,
    int ResultCount) : Synapse;

public sealed record McpToolDenied(
    string Tool,
    string ClientId,
    string Reason) : Synapse;

public sealed record McpApprovalRequired(
    string Tool,
    string ClientId,
    string BundleId) : Synapse;

public sealed record McpUserApproved(string BundleId, string Tool) : Synapse;

public sealed record BehaviorActivateAsked(string BehaviorId) : Synapse;

public sealed record BehaviorActivateCompleted(string BehaviorId) : Synapse;

// Northbound MCP gateway: journal invoke, ask introspection, complete; mutating tools need approval.
public sealed class McpGateway : Neuron<McpGatewayState>,
    INeuron<McpToolInvokeRequested>,
    INeuron<ActiveNeuronsAnswered>,
    INeuron<McpUserApproved>
{
    public const string ToolListActive = "list_active_neurons";
    public const string ToolActivateBehavior = "activate_behavior";

    public Task HandleAsync(McpToolInvokeRequested fact, CancellationToken cancellationToken)
    {
        // Owner binding comes from the request surface (token), never from free-form tool args.
        Emit(new McpToolInvoked(fact.Tool, fact.ArgsHash, fact.ClientId));

        if (fact.Mutating)
        {
            var bundleId = $"mcp-{fact.ClientId}-{fact.Tool}";
            State.PendingBundleId = bundleId;
            State.PendingTool = fact.Tool;
            State.PendingClientId = fact.ClientId;
            Emit(new McpApprovalRequired(fact.Tool, fact.ClientId, bundleId));
            return Task.CompletedTask;
        }

        if (string.Equals(fact.Tool, ToolListActive, StringComparison.Ordinal))
        {
            State.PendingTool = fact.Tool;
            State.PendingClientId = fact.ClientId;
            Ask<ActiveNeuronsAnswered>(new ActiveNeuronsAsked(fact.OwnerBound));
            return Task.CompletedTask;
        }

        Emit(new McpToolDenied(fact.Tool, fact.ClientId, Reason: "unknown-tool"));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ActiveNeuronsAnswered fact, CancellationToken cancellationToken)
    {
        if (!string.Equals(State.PendingTool, ToolListActive, StringComparison.Ordinal)
            || State.PendingClientId is null)
        {
            return Task.CompletedTask;
        }

        Emit(new McpToolCompleted(
            ToolListActive,
            State.PendingClientId,
            Ok: true,
            ResultCount: fact.Neurons.Length));
        State.PendingTool = null;
        State.PendingClientId = null;
        return Task.CompletedTask;
    }

    public Task HandleAsync(McpUserApproved fact, CancellationToken cancellationToken)
    {
        if (State.PendingBundleId is null
            || !string.Equals(fact.BundleId, State.PendingBundleId, StringComparison.Ordinal)
            || !string.Equals(fact.Tool, State.PendingTool, StringComparison.Ordinal)
            || State.PendingClientId is null)
        {
            return Task.CompletedTask;
        }

        Emit(new BehaviorActivateAsked(BehaviorId: "ide-federation-probe"));
        Emit(new BehaviorActivateCompleted(BehaviorId: "ide-federation-probe"));
        Emit(new McpToolCompleted(
            fact.Tool,
            State.PendingClientId,
            Ok: true,
            ResultCount: 1));
        State.PendingBundleId = null;
        State.PendingTool = null;
        State.PendingClientId = null;
        return Task.CompletedTask;
    }
}

public sealed class McpGatewayState
{
    public string? PendingBundleId { get; set; }
    public string? PendingTool { get; set; }
    public string? PendingClientId { get; set; }
}

public sealed class IntrospectionCatalog : Neuron, IAnswers<ActiveNeuronsAsked, ActiveNeuronsAnswered>
{
    public Task<ActiveNeuronsAnswered?> HandleAsync(
        ActiveNeuronsAsked question, CancellationToken cancellationToken)
        => Task.FromResult<ActiveNeuronsAnswered?>(new(
            question.OwnerBound,
            Neurons:
            [
                new ActiveNeuronRow("mcpgateway", question.OwnerBound),
                new ActiveNeuronRow("chat", "owner-desk"),
            ]));
}

// Catalog sinks for ambient MCP / activation facts.
public sealed class McpAuditLedger : Neuron,
    INeuron<McpToolInvoked>,
    INeuron<McpToolCompleted>,
    INeuron<McpToolDenied>,
    INeuron<McpApprovalRequired>,
    INeuron<BehaviorActivateAsked>,
    INeuron<BehaviorActivateCompleted>
{
    public Task HandleAsync(McpToolInvoked fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(McpToolCompleted fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(McpToolDenied fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(McpApprovalRequired fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(BehaviorActivateAsked fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(BehaviorActivateCompleted fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
