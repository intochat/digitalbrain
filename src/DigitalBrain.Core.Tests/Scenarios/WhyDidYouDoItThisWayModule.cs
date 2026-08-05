namespace DigitalBrain.Core.Tests.Scenarios;

// Standing owner instruction — must be journaled before any action that claims it as justification.
public sealed record UserInstruction(string Text, string Scope) : Synapse;

// Edge/session command that causes a consequential agent action under the standing instruction.
public sealed record PerformOutboundAction(string Action, string Detail) : Synapse;

public sealed record AgentActionTaken(string Action, string Detail, string AppliedScope) : Synapse;

public sealed record WhyAsked(string AboutAction) : Synapse;

// Answer cites the journaled instruction text — never a free-form rationalization without that fact.
public sealed record WhyAnswer(
    string InstructionText,
    string InstructionScope,
    string Action,
    string Detail) : Synapse;

public sealed class InstructionalAgentState
{
    public string? InstructionText { get; set; }
    public string? InstructionScope { get; set; }
}

// Memory + actor + explainer mock: State is filled only when UserInstruction is journaled as heard.
public sealed class InstructionalAgent : Neuron<InstructionalAgentState>,
    INeuron<UserInstruction>,
    INeuron<PerformOutboundAction>,
    IAnswers<WhyAsked, WhyAnswer>
{
    public Task HandleAsync(UserInstruction instruction, CancellationToken cancellationToken)
    {
        State.InstructionText = instruction.Text;
        State.InstructionScope = instruction.Scope;
        return Task.CompletedTask;
    }

    public Task HandleAsync(PerformOutboundAction command, CancellationToken cancellationToken)
    {
        if (State.InstructionText is null || State.InstructionScope is null)
        {
            throw new InvalidOperationException(
                "PerformOutboundAction requires a prior journaled UserInstruction in this context.");
        }

        Emit(new AgentActionTaken(command.Action, command.Detail, State.InstructionScope));
        return Task.CompletedTask;
    }

    public Task<WhyAnswer?> HandleAsync(WhyAsked question, CancellationToken cancellationToken)
    {
        if (State.InstructionText is null || State.InstructionScope is null)
        {
            return Task.FromResult<WhyAnswer?>(null);
        }

        return Task.FromResult<WhyAnswer?>(new WhyAnswer(
            State.InstructionText,
            State.InstructionScope,
            question.AboutAction,
            Detail: $"Because you instructed: {State.InstructionText}"));
    }
}

// Ambient action sink — required catalog listener for AgentActionTaken Emit.
public sealed class ActionLedger : Neuron, INeuron<AgentActionTaken>
{
    public Task HandleAsync(AgentActionTaken fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
