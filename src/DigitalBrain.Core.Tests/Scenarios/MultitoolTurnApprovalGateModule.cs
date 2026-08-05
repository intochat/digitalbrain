namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record MultitoolUserMessaged(string Text) : Synapse;

public sealed record CapabilityToolSelected(string ToolName, string Goal) : Synapse;

public sealed record ApprovalRequired(string BundleId, IReadOnlyList<string> Tools) : Synapse;

public sealed record UserApproved(string BundleId, IReadOnlyList<string> GrantedTools) : Synapse;

public sealed record ToolCompleted(string ToolName, string BundleId, string Result) : Synapse;

public sealed record MultitoolAssistantSaid(string Text) : Synapse;

// Planner: select tools as facts, gate side effects on UserApproved (deferred turn — never same-turn await).
public sealed class MultitoolAssistant : Neuron<MultitoolAssistantState>,
    INeuron<MultitoolUserMessaged>,
    INeuron<UserApproved>
{
    public const string ToolAccountPull = "account-pull";
    public const string ToolEmailDraft = "email-draft";

    public Task HandleAsync(MultitoolUserMessaged fact, CancellationToken cancellationToken)
    {
        var bundleId = $"bundle-{fact.Text.GetHashCode(StringComparison.Ordinal):x8}";
        State.PendingBundleId = bundleId;
        State.PendingGoal = fact.Text;
        State.SelectedTools = [ToolAccountPull, ToolEmailDraft];

        Emit(new CapabilityToolSelected(ToolAccountPull, fact.Text));
        Emit(new CapabilityToolSelected(ToolEmailDraft, fact.Text));
        Emit(new ApprovalRequired(bundleId, State.SelectedTools));
        return Task.CompletedTask;
    }

    public Task HandleAsync(UserApproved fact, CancellationToken cancellationToken)
    {
        if (State.PendingBundleId is null
            || !string.Equals(fact.BundleId, State.PendingBundleId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        if (State.ToolsCompleted)
        {
            return Task.CompletedTask;
        }

        var granted = fact.GrantedTools;
        foreach (var tool in State.SelectedTools)
        {
            if (!granted.Contains(tool, StringComparer.Ordinal))
            {
                continue;
            }

            Emit(new ToolCompleted(tool, fact.BundleId, Result: $"ok:{tool}"));
        }

        State.ToolsCompleted = true;
        Emit(new MultitoolAssistantSaid(
            $"Completed {granted.Count} tool(s) for {State.PendingGoal} after approval."));
        return Task.CompletedTask;
    }
}

public sealed class MultitoolAssistantState
{
    public string? PendingBundleId { get; set; }
    public string? PendingGoal { get; set; }
    public IReadOnlyList<string> SelectedTools { get; set; } = [];
    public bool ToolsCompleted { get; set; }
}

// Catalog sinks so ambient Emit of approval / tools / assistant speech is legal.
public sealed class ApprovalTray : Neuron, INeuron<ApprovalRequired>, INeuron<CapabilityToolSelected>
{
    public Task HandleAsync(ApprovalRequired fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(CapabilityToolSelected fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class ToolLedger : Neuron, INeuron<ToolCompleted>
{
    public Task HandleAsync(ToolCompleted fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class MultitoolSpeechLedger : Neuron, INeuron<MultitoolAssistantSaid>
{
    public Task HandleAsync(MultitoolAssistantSaid fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
