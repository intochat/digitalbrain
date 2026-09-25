using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.CSharpExpert;

[GrainType("csharp-expert.run")]
internal sealed class CodingRunNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<CodingRunState> state)
    : Neuron, ICodingRun
{
    private string Key => this.GetPrimaryKeyString();

    private CodingRunState Current => state.State ?? CodingRunState.Empty;

    public async Task Request(FeatureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        await PersistAsync(CodingRunState.Empty with { Request = request, Status = CodingRunStatus.Requested },
            new FeatureRequested(Key, request));
    }

    public async Task RecordContext(ProjectModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var current = Current;
        if (current.Request is null)
        {
            throw new InvalidOperationException("Record the feature request before its context.");
        }

        await PersistAsync(current with { Model = model, Status = CodingRunStatus.ContextReady }, new ContextReady(Key, model));
    }

    public async Task RecordPlan(CodingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var current = Current;
        if (current.Model is null)
        {
            throw new InvalidOperationException("Record the project context before a plan.");
        }

        await PersistAsync(current with { Plan = plan, Status = CodingRunStatus.PlanDrafted }, new PlanDrafted(Key, plan));
    }

    public async Task Fail(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var current = Current;
        await PersistAsync(current with { Status = CodingRunStatus.Failed, FailureReason = reason }, new RunFailed(Key, reason));
    }

    [ReadOnly]
    public Task<CodingRunSnapshot> Read()
    {
        var current = Current;
        return Task.FromResult(new CodingRunSnapshot(Key, current.Status, current.Request, current.Model, current.Plan, current.FailureReason));
    }

    private async Task PersistAsync(CodingRunState next, Signal changed)
    {
        state.State = next;
        await state.WriteStateAsync();
        await PublishAsync(changed);
    }
}
