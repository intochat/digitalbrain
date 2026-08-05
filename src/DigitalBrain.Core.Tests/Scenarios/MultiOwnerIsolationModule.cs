using DigitalBrain.Mocks;

namespace DigitalBrain.Core.Tests.Scenarios;

// Stage-1: Ada/Beau isolation via distinct context Names (same module kinds).

public sealed record RunwayCashSnapshot(string Owner, decimal CashUsd) : Synapse;

public sealed record RunwayAsked(string Owner) : Synapse;

public sealed record RunwayAnswer(string Owner, decimal CashUsd, string SourceMailSubject) : Synapse;

public sealed class RunwayAdvisorState
{
    public decimal CashUsd { get; set; }
    public string? LastMailSubject { get; set; }
}

// Same kind for both owners; Name is the owner context. Hears only own EmailReceived + cash seed.
public sealed class RunwayAdvisor : Neuron<RunwayAdvisorState>,
    INeuron<EmailReceived>,
    INeuron<RunwayCashSnapshot>,
    IAnswers<RunwayAsked, RunwayAnswer>
{
    public Task HandleAsync(EmailReceived fact, CancellationToken cancellationToken)
    {
        State.LastMailSubject = fact.Subject;
        return Task.CompletedTask;
    }

    public Task HandleAsync(RunwayCashSnapshot fact, CancellationToken cancellationToken)
    {
        State.CashUsd = fact.CashUsd;
        return Task.CompletedTask;
    }

    public Task<RunwayAnswer?> HandleAsync(RunwayAsked question, CancellationToken cancellationToken)
        => Task.FromResult<RunwayAnswer?>(new RunwayAnswer(
            Id.Name,
            State.CashUsd,
            SourceMailSubject: State.LastMailSubject ?? ""));
}
