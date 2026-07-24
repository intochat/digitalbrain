using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Salesforce;
using DigitalBrain.Tasks;
using Microsoft.Extensions.AI;

namespace DigitalBrain.ModuleTests;

public sealed partial class ModuleDriverModule : IModule;

[ClientEntryPoint]
public partial interface IModuleDriver : INeuron
{
    [Alias(nameof(RunDirectAgent))]
    Task<string> RunDirectAgent(NeuronId target, string prompt);
}

public partial interface IProbeTarget : INeuron
{
    [Alias(nameof(Probe))]
    Task Probe();
}

public partial interface IRollbackProbe : INeuron
{
    [Alias(nameof(IncrementCapability))]
    Task<int> IncrementCapability();
}

public partial interface IAnnouncer : INeuron;

public partial interface INoticeListener : INeuron;

public partial interface INoticeAudit : INeuron;

public partial interface ICycleProbe : INeuron;

public partial interface IAlphaProbe : INeuron;

public partial interface IBetaProbe : INeuron;

public partial interface IModuleConcurrent : IAgent;

public partial interface IModuleGroupChat : IGroupChat;

public partial interface IScriptedWorker : IWorker;

[GenerateSerializer]
[Alias("module-tests.ping")]
public sealed record ProbePing([property: Id(0)] string Value) : Synapse;

[GenerateSerializer]
[Alias("module-tests.pong")]
public sealed record ProbePong([property: Id(0)] string Value) : Synapse;

[GenerateSerializer]
[Alias("module-tests.invoke-target")]
public sealed record InvokeTarget([property: Id(0)] NeuronId Target) : Synapse;

[GenerateSerializer]
[Alias("module-tests.authorization-observed")]
public sealed record AuthorizationObserved(
    [property: Id(0)] bool Authorized,
    [property: Id(1)] string? Failure) : Synapse;

[GenerateSerializer]
[Alias("module-tests.increment")]
public sealed record Increment : Synapse;

[GenerateSerializer]
[Alias("module-tests.counted")]
public sealed record Counted([property: Id(0)] int Count) : Synapse;

[GenerateSerializer]
[Alias("module-tests.read-count")]
public sealed record ReadCount : Synapse;

[GenerateSerializer]
[Alias("module-tests.count-observed")]
public sealed record CountObserved([property: Id(0)] int Count) : Synapse;

[GenerateSerializer]
[Alias("module-tests.invoke-rollback")]
public sealed record InvokeRollback([property: Id(0)] NeuronId Target) : Synapse;

[GenerateSerializer]
[Alias("module-tests.announce")]
public sealed record Announce : Synapse;

[GenerateSerializer]
[Alias("module-tests.notice")]
public sealed record Notice : Synapse;

[GenerateSerializer]
[Alias("module-tests.notice-seen")]
public sealed record NoticeSeen : Synapse;

[GenerateSerializer]
[Alias("module-tests.notice-audited")]
public sealed record NoticeAudited : Synapse;

[GenerateSerializer]
[Alias("module-tests.loop")]
public sealed record LoopSignal : Synapse;

[GenerateSerializer]
[Alias("module-tests.loop-observed")]
public sealed record LoopObserved([property: Id(0)] int Count) : Synapse;

[GenerateSerializer]
[Alias("module-tests.cross-silo")]
public sealed record CrossSilo : Synapse;

[GenerateSerializer]
[Alias("module-tests.cross-silo-arrived")]
public sealed record CrossSiloArrived : Synapse;

[GenerateSerializer]
[Alias("module-tests.read-beta-marker")]
public sealed record ReadBetaMarker : Synapse;

[GenerateSerializer]
[Alias("module-tests.beta-marker")]
public sealed record BetaMarker([property: Id(0)] Guid Activation) : Synapse;

[GenerateSerializer]
[Alias("module-tests.module-goal")]
public sealed record ModuleGoal([property: Id(0)] string Script) : Goal;

[GenerateSerializer]
[Alias("module-tests.module-result")]
public sealed record ModuleResult([property: Id(0)] string Value) : Result;

[GenerateSerializer]
[Alias("module-tests.module-failure")]
public sealed record ModuleFailure([property: Id(0)] string Value) : Failure;

[GenerateSerializer]
[Alias("module-tests.start-task")]
public sealed record StartModuleTask(
    [property: Id(0)] NeuronId Task,
    [property: Id(1)] StartTask Command) : Synapse;

[GenerateSerializer]
[Alias("module-tests.cancel-task")]
public sealed record CancelModuleTask(
    [property: Id(0)] NeuronId Task,
    [property: Id(1)] CancelTask Command) : Synapse;

[GenerateSerializer]
[Alias("module-tests.read-task")]
public sealed record ReadModuleTask([property: Id(0)] NeuronId Task) : Synapse;

[GenerateSerializer]
[Alias("module-tests.task-observed")]
public sealed record TaskObserved(
    [property: Id(0)] string Operation,
    [property: Id(1)] TaskSnapshot Snapshot) : Synapse;

[GenerateSerializer]
[Alias("module-tests.read-gmail")]
public sealed record ReadGmail(
    [property: Id(0)] NeuronId Gmail,
    [property: Id(1)] string MessageId) : Synapse;

[GenerateSerializer]
[Alias("module-tests.gmail-read")]
public sealed record GmailRead([property: Id(0)] GmailMessage Message) : Synapse;

[GenerateSerializer]
[Alias("module-tests.propose-salesforce")]
public sealed record ProposeSalesforce(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string AccountId,
    [property: Id(2)] string Description) : Synapse;

[GenerateSerializer]
[Alias("module-tests.salesforce-proposed")]
public sealed record SalesforceProposed(
    [property: Id(0)] SalesforceAccountDescriptionMutation Mutation) : Synapse;

[GenerateSerializer]
[Alias("module-tests.apply-salesforce-approval")]
public sealed record ApplySalesforceApproval(
    [property: Id(0)] SalesforceMutationApproval Approval) : Synapse;

[GenerateSerializer]
[Alias("module-tests.salesforce-approved")]
public sealed record SalesforceApproved(
    [property: Id(0)] SalesforceMutationState State,
    [property: Id(1)] string? Failure) : Synapse;

internal sealed class ProbeTarget :
    Neuron,
    IProbeTarget,
    IHandle<ProbePing>,
    IEmit<ProbePong>
{
    public Task Probe() => Task.CompletedTask;

    public Task HandleAsync(
        ProbePing synapse,
        CancellationToken cancellationToken)
        => EmitAsync(new ProbePong(synapse.Value));
}

internal sealed class RollbackProbe :
    Neuron,
    IRollbackProbe,
    IHandle<Increment>,
    IHandle<ReadCount>,
    IEmit<Counted>,
    IEmit<CountObserved>
{
    private int _count;

    public async Task HandleAsync(
        Increment synapse,
        CancellationToken cancellationToken)
    {
        _count++;
        EnlistTurnRollback(() => _count--);
        await EmitAsync(new Counted(_count));
    }

    public async Task<int> IncrementCapability()
    {
        _count++;
        EnlistTurnRollback(() => _count--);
        await EmitAsync(new Counted(_count));
        return _count;
    }

    public Task HandleAsync(
        ReadCount synapse,
        CancellationToken cancellationToken)
        => EmitAsync(new CountObserved(_count));
}

internal sealed class Announcer :
    Neuron,
    IAnnouncer,
    IHandle<Announce>,
    IEmit<Notice>
{
    public Task HandleAsync(
        Announce synapse,
        CancellationToken cancellationToken)
        => EmitAsync(new Notice());
}

internal sealed class NoticeListener :
    Neuron,
    INoticeListener,
    IHandle<Notice>,
    IEmit<NoticeSeen>
{
    public Task HandleAsync(
        Notice synapse,
        CancellationToken cancellationToken)
        => EmitAsync(new NoticeSeen());
}

internal sealed class NoticeAudit :
    Neuron,
    INoticeAudit,
    IHandle<Notice>,
    IEmit<NoticeAudited>
{
    public Task HandleAsync(
        Notice synapse,
        CancellationToken cancellationToken)
        => EmitAsync(new NoticeAudited());
}

internal sealed class CycleProbe :
    Neuron,
    ICycleProbe,
    IHandle<LoopSignal>,
    IEmit<LoopSignal>,
    IEmit<LoopObserved>
{
    private int _count;

    public async Task HandleAsync(
        LoopSignal synapse,
        CancellationToken cancellationToken)
    {
        _count++;
        await EmitAsync(new LoopObserved(_count));
        await EmitAsync(new LoopSignal());
    }
}

[PinToSilo("alpha")]
internal sealed class AlphaProbe :
    Neuron,
    IAlphaProbe,
    IHandle<CrossSilo>,
    IEmit<CrossSilo>
{
    public Task HandleAsync(
        CrossSilo synapse,
        CancellationToken cancellationToken)
        => SendAsync(
            NeuronId.For<IBetaProbe>(Id.Owner, "beta"),
            new CrossSilo());
}

[PinToSilo("beta")]
internal sealed class BetaProbe :
    Neuron,
    IBetaProbe,
    IHandle<CrossSilo>,
    IHandle<ReadBetaMarker>,
    IEmit<CrossSiloArrived>,
    IEmit<BetaMarker>
{
    private readonly Guid _activation = Guid.NewGuid();

    public Task HandleAsync(
        CrossSilo synapse,
        CancellationToken cancellationToken)
        => EmitAsync(new CrossSiloArrived());

    public Task HandleAsync(
        ReadBetaMarker synapse,
        CancellationToken cancellationToken)
        => EmitAsync(new BetaMarker(_activation));
}

internal sealed class ModuleConcurrent : Concurrent, IModuleConcurrent
{
    protected override IReadOnlyList<Participant> Participants =>
        [Participant<ILlama32>("direct-model")];
}

internal sealed class ModuleGroupChat : GroupChat, IModuleGroupChat
{
    protected override IReadOnlyList<Participant> Participants =>
        [Participant<ILlama32>("group-model")];

    protected override IReadOnlyList<ChatMessage> CreateMessages(Goal goal)
        => [new ChatMessage(ChatRole.User, ((ModuleGoal)goal).Script)];

    protected override Result CreateResult(IReadOnlyList<ChatMessage> messages)
        => new ModuleResult(messages[^1].Text);
}

internal sealed class ScriptedWorker :
    Neuron,
    IScriptedWorker,
    IEmit<AttemptAccepted>,
    IEmit<AttemptProgressed>,
    IEmit<AttemptWaiting>,
    IEmit<AttemptSucceeded>,
    IEmit<AttemptFailed>,
    IEmit<AttemptCancelled>
{
    private readonly Dictionary<NeuronId, string> _scripts = [];

    public async Task Accept(AttemptRequest request)
    {
        var script = ((ModuleGoal)request.Goal).Script;
        _scripts[request.Task] = script;

        switch (script)
        {
            case "hold":
            case "cancel":
                await SendAsync(request.Task, new AttemptAccepted(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision));
                break;
            case "wait":
                await SendAsync(request.Task, new AttemptWaiting(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision,
                    new InputRequired(new BlockerId(Guid.Parse(
                        "1137eb4c-a173-47d8-8084-038e0896985a")))));
                break;
            case "progress":
                await SendAsync(request.Task, new AttemptAccepted(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision));
                await SendAsync(request.Task, new AttemptProgressed(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision));
                break;
            case "failure":
                await SendAsync(request.Task, new AttemptFailed(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision,
                    new ModuleFailure("expected failure"),
                    Retryable: false));
                break;
            default:
                await SendAsync(request.Task, new AttemptSucceeded(
                    request.Task,
                    request.Worker,
                    request.Attempt,
                    request.Revision,
                    new ModuleResult("done"),
                    []));
                break;
        }
    }

    public Task Continue(AttemptCursor cursor)
        => SendAsync(cursor.Task, new AttemptSucceeded(
            cursor.Task,
            cursor.Worker,
            cursor.Attempt,
            cursor.Revision,
            new ModuleResult("continued"),
            []));

    public Task Cancel(AttemptCursor cursor)
        => SendAsync(cursor.Task, new AttemptCancelled(
            cursor.Task,
            cursor.Worker,
            cursor.Attempt,
            cursor.Revision));
}

internal sealed class ModuleDriver :
    Neuron,
    IModuleDriver,
    IHandle<InvokeTarget>,
    IHandle<InvokeRollback>,
    IHandle<StartModuleTask>,
    IHandle<CancelModuleTask>,
    IHandle<ReadModuleTask>,
    IHandle<ReadGmail>,
    IHandle<ProposeSalesforce>,
    IHandle<SalesforceMutationApproval>,
    IHandle<ApplySalesforceApproval>,
    IEmit<AuthorizationObserved>,
    IEmit<TaskObserved>,
    IEmit<GmailRead>,
    IEmit<SalesforceProposed>,
    IEmit<SalesforceApproved>
{
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The acceptance driver reports the authorization result as typed evidence.")]
    public async Task HandleAsync(
        InvokeTarget synapse,
        CancellationToken cancellationToken)
    {
        try
        {
            await Reference<IProbeTarget>(synapse.Target).Probe();
            await EmitAsync(new AuthorizationObserved(true, null));
        }
        catch (Exception failure)
        {
            await EmitAsync(new AuthorizationObserved(
                false,
                failure.GetType().Name));
        }
    }

    public async Task HandleAsync(
        InvokeRollback synapse,
        CancellationToken cancellationToken)
    {
        var target = Reference<IRollbackProbe>(synapse.Target);

        try
        {
            _ = await target.IncrementCapability();
        }
        catch (InvalidOperationException)
        {
        }

        await EmitAsync(new CountObserved(
            await target.IncrementCapability()));
    }

    public async Task<string> RunDirectAgent(NeuronId target, string prompt)
        => (await Reference<IModuleConcurrent>(target)
            .Respond([new ChatMessage(ChatRole.User, prompt)])).Text;

    public async Task HandleAsync(
        StartModuleTask synapse,
        CancellationToken cancellationToken)
    {
        var snapshot = await Reference<ITask>(synapse.Task).Start(synapse.Command);
        await EmitAsync(new TaskObserved(nameof(ITask.Start), snapshot));
    }

    public async Task HandleAsync(
        CancelModuleTask synapse,
        CancellationToken cancellationToken)
    {
        var snapshot = await Reference<ITask>(synapse.Task).Cancel(synapse.Command);
        await EmitAsync(new TaskObserved(nameof(ITask.Cancel), snapshot));
    }

    public async Task HandleAsync(
        ReadModuleTask synapse,
        CancellationToken cancellationToken)
    {
        var snapshot = await Reference<ITask>(synapse.Task).Read();
        await EmitAsync(new TaskObserved(nameof(ITask.Read), snapshot));
    }

    public async Task HandleAsync(
        ReadGmail synapse,
        CancellationToken cancellationToken)
    {
        var message = await Reference<IGmail>(synapse.Gmail)
            .ReadMessage(synapse.MessageId, cancellationToken);
        await EmitAsync(new GmailRead(message));
    }

    public async Task HandleAsync(
        ProposeSalesforce synapse,
        CancellationToken cancellationToken)
    {
        var mutation = await Salesforce().ProposeAccountDescription(
            synapse.CommandId,
            Id,
            synapse.AccountId,
            synapse.Description,
            cancellationToken);
        await EmitAsync(new SalesforceProposed(mutation));
    }

    public Task HandleAsync(
        SalesforceMutationApproval synapse,
        CancellationToken cancellationToken)
        => SendAsync(Id, new ApplySalesforceApproval(synapse));

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The acceptance driver turns the provider-boundary result into typed evidence.")]
    public async Task HandleAsync(
        ApplySalesforceApproval synapse,
        CancellationToken cancellationToken)
    {
        try
        {
            var incoming = await ReadJournal(JournalKind.Incoming, afterSequence: 0);
            var evidence = incoming.Delta.Single(delivery =>
                delivery.Caller == synapse.Approval.Approver
                && delivery.Synapse is SalesforceMutationApproval recorded
                && recorded == synapse.Approval);
            var mutation = await Salesforce().ApproveAccountDescription(
                synapse.Approval,
                evidence,
                cancellationToken);
            await EmitAsync(new SalesforceApproved(mutation.State, null));
        }
        catch (Exception failure)
        {
            await EmitAsync(new SalesforceApproved(
                SalesforceMutationState.AwaitingApproval,
                failure.GetType().Name));
        }
    }

    private ISalesforce Salesforce()
        => GrainFactory.GetGrain<ISalesforce>($"{Id.Owner.Value}/salesforce");

    private T Reference<T>(NeuronId target)
        where T : class, INeuron
        => GrainFactory.GetGrain<T>($"{target.Owner.Value}/{target.Name}");
}
