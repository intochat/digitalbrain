namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record OpenWorkThread(string ThreadKey, string Title) : Synapse;

public sealed record WorkThreadOpened(string ThreadKey, string Title) : Synapse;

public sealed record OpportunityLinked(string ThreadKey, string OpportunityId) : Synapse;

public sealed record EmailThreadAttached(string ThreadKey, string MessageId) : Synapse;

public sealed record ThreadTimelineReady(string ThreadKey) : Synapse;

// Hop 1: session intent → durable work-thread root fact.
public sealed class ThreadCoordinator : Neuron, INeuron<OpenWorkThread>
{
    public Task HandleAsync(OpenWorkThread fact, CancellationToken cancellationToken)
    {
        Emit(new WorkThreadOpened(fact.ThreadKey, fact.Title));
        return Task.CompletedTask;
    }
}

// Hop 2: CRM attaches opportunity under the same thread key, Cause-linked to hop 1.
public sealed class CrmLinker : Neuron, INeuron<WorkThreadOpened>
{
    public Task HandleAsync(WorkThreadOpened fact, CancellationToken cancellationToken)
    {
        Emit(new OpportunityLinked(fact.ThreadKey, OpportunityId: $"opp-{fact.ThreadKey}"));
        return Task.CompletedTask;
    }
}

// Hop 3: mail correlator attaches related email under the opportunity link.
public sealed class MailLinker : Neuron, INeuron<OpportunityLinked>
{
    public Task HandleAsync(OpportunityLinked fact, CancellationToken cancellationToken)
    {
        Emit(new EmailThreadAttached(fact.ThreadKey, MessageId: $"mail-{fact.OpportunityId}"));
        return Task.CompletedTask;
    }
}

// Hop 4: timeline projector materializes one thread view from the third-hop fact + chain refs.
public sealed class ThreadTimeline : Neuron, INeuron<EmailThreadAttached>
{
    public Task HandleAsync(EmailThreadAttached fact, CancellationToken cancellationToken)
    {
        Emit(new ThreadTimelineReady(fact.ThreadKey));
        return Task.CompletedTask;
    }
}

// Catalog sink for the terminal timeline fact so ThreadTimelineReady Emit is legal.
public sealed class ThreadTimelineLedger : Neuron, INeuron<ThreadTimelineReady>
{
    public Task HandleAsync(ThreadTimelineReady fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
