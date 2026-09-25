using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.DotNet;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.CSharpExpert;

[GrainType("csharp-expert.run")]
internal sealed class CodingRunNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<CodingRunState> state)
    : Neuron, ICodingRun
{
    private string Key => this.GetPrimaryKeyString();

    private CodingRunState Current => state.RecordExists ? state.State : CodingRunState.Empty;

    public async Task Request(FeatureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Current.Request is not null && Current.Status != CodingRunStatus.Failed)
        {
            throw new InvalidOperationException("This run is already in progress.");
        }

        await PersistAsync(CodingRunState.Empty with { Request = request, Status = CodingRunStatus.Requested },
            new FeatureRequested(Key, request));
    }

    public async Task Clarify(string clarification)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clarification);
        EnsureOpen();
        var current = Current;
        if (current.Request is null)
        {
            throw new InvalidOperationException("Request the feature before clarifying its plan.");
        }

        if (current.Status is not (CodingRunStatus.ContextReady or CodingRunStatus.PlanDrafted))
        {
            throw new InvalidOperationException("Clarify the plan before it is approved.");
        }

        var clarifications = current.Clarifications.Append(clarification).ToArray();
        await PersistAsync(current with { Clarifications = clarifications }, new PlanClarified(Key, clarification));
    }

    public async Task Approve()
    {
        EnsureOpen();
        var current = Current;
        if (current.Plan is null || current.Status != CodingRunStatus.PlanDrafted)
        {
            throw new InvalidOperationException("Approve a drafted plan that is waiting for approval.");
        }

        await PersistAsync(current with { Status = CodingRunStatus.PlanApproved }, new PlanApproved(Key, current.Plan));
    }

    public async Task Stop()
    {
        var current = Current;
        if (IsClosed(current.Status))
        {
            return;
        }

        await PersistAsync(current with { Status = CodingRunStatus.Stopped }, new RunStopped(Key));
    }

    public async Task RecordContext(ProjectModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        EnsureOpen();
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
        EnsureOpen();
        var current = Current;
        if (current.Model is null || current.Status is not (CodingRunStatus.ContextReady or CodingRunStatus.PlanDrafted))
        {
            throw new InvalidOperationException("Record a plan while the run is planning.");
        }

        await PersistAsync(current with { Plan = plan, Status = CodingRunStatus.PlanDrafted }, new PlanDrafted(Key, plan));
    }

    public async Task PrepareWorkspace(string workspaceRoot, string workspaceSolutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceSolutionPath);
        EnsureOpen();
        var current = Current;
        if (current.Plan is null || current.Status != CodingRunStatus.PlanApproved)
        {
            throw new InvalidOperationException("Prepare the workspace after the plan is approved.");
        }

        await PersistAsync(current with
        {
            WorkspaceRoot = workspaceRoot,
            WorkspaceSolutionPath = workspaceSolutionPath,
            Status = CodingRunStatus.WorkspaceReady,
        }, new WorkspacePrepared(Key, workspaceSolutionPath));
    }

    public async Task RecordStepDrafted(int stepNumber, string diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        EnsureOpen();
        var current = Current;
        EnsureEditing(current, stepNumber);
        await PersistAsync(current with
        {
            Status = CodingRunStatus.StepDrafted,
            Diff = diff,
            Diagnostics = [],
            FixAttempts = NextFixAttempts(current),
        }, new StepDrafted(Key, stepNumber, diff));
    }

    public async Task RecordBrokenEdit(int stepNumber, string diff, IReadOnlyList<DiagnosticGroup> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diff);
        ArgumentNullException.ThrowIfNull(diagnostics);
        EnsureOpen();
        var current = Current;
        EnsureEditing(current, stepNumber);
        await PersistAsync(current with
        {
            Status = CodingRunStatus.BuildFailed,
            Diff = diff,
            Diagnostics = diagnostics,
            FixAttempts = NextFixAttempts(current),
        }, new BuildFailed(Key, stepNumber, diagnostics));
    }

    public async Task RecordBuild(BuildOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        EnsureOpen();
        var current = Current;
        if (current.Status != CodingRunStatus.StepDrafted)
        {
            throw new InvalidOperationException("Record a build after a step is drafted.");
        }

        var step = CurrentStep(current);
        if (outcome.Succeeded)
        {
            await PersistAsync(current with { Status = CodingRunStatus.BuildPassed, Build = outcome, Diagnostics = [] },
                new BuildPassed(Key, step, outcome.WarningCount));
            return;
        }

        var diagnostics = DiagnosticGroups.From(outcome.Errors);
        await PersistAsync(current with { Status = CodingRunStatus.BuildFailed, Build = outcome, Diagnostics = diagnostics },
            new BuildFailed(Key, step, diagnostics));
    }

    public async Task RecordTests(TestOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        EnsureOpen();
        var current = Current;
        if (current.Status != CodingRunStatus.BuildPassed)
        {
            throw new InvalidOperationException("Record tests after the build passes.");
        }

        var step = CurrentStep(current);
        if (!outcome.Succeeded)
        {
            await PersistAsync(current with { Status = CodingRunStatus.TestsFailed, Test = outcome },
                new TestsFailed(Key, step, outcome.Failures));
            return;
        }

        await PersistAsync(current with { Status = CodingRunStatus.TestsPassed, Test = outcome },
            new TestsPassed(Key, step, outcome.Passed, outcome.Total));
    }

    public async Task RecordReview(IReadOnlyList<string> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        EnsureOpen();
        var current = Current;
        if (current.Status != CodingRunStatus.TestsPassed)
        {
            throw new InvalidOperationException("Record a review after the tests pass.");
        }

        var step = CurrentStep(current);
        if (findings.Count > 0)
        {
            await PersistAsync(current with { Status = CodingRunStatus.ReviewRejected, ReviewFindings = findings },
                new ReviewRejected(Key, step, findings));
            return;
        }

        await AdvanceAsync(current with { ReviewFindings = [] }, step);
    }

    public async Task RecordNeedsHuman(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        EnsureOpen();
        await PersistAsync(Current with { Status = CodingRunStatus.NeedsHuman, FailureReason = reason }, new NeedsHuman(Key, reason));
    }

    public async Task Fail(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (IsClosed(Current.Status))
        {
            return;
        }

        await PersistAsync(Current with { Status = CodingRunStatus.Failed, FailureReason = reason }, new RunFailed(Key, reason));
    }

    [ReadOnly]
    public Task<CodingRunSnapshot> Read()
    {
        var current = Current;
        var total = current.Plan?.Steps.Count ?? 0;
        return Task.FromResult(new CodingRunSnapshot(
            Key, current.Status, current.Request, current.Model, current.Plan, current.FailureReason, current.Clarifications,
            current.WorkspaceRoot, current.WorkspaceSolutionPath, total == 0 ? 0 : Math.Min(current.StepsCompleted + 1, total), total,
            current.FixAttempts, current.Diff, current.Build, current.Test, current.Diagnostics,
            current.ReviewFindings, current.CompletedStepDiffs));
    }

    private async Task AdvanceAsync(CodingRunState current, int stepNumber)
    {
        var total = current.Plan?.Steps.Count ?? 0;
        var completed = Math.Max(current.StepsCompleted, stepNumber);
        var closed = current with { StepsCompleted = completed, CompletedStepDiffs = [.. current.CompletedStepDiffs, current.Diff ?? string.Empty] };
        if (completed >= total)
        {
            // Keep the final step's fix attempts in the finished snapshot; a fresh step clears them.
            await PersistAsync(closed with { Status = CodingRunStatus.Finished },
                new RunFinished(Key, string.Join(Environment.NewLine, closed.CompletedStepDiffs)));
            return;
        }

        await PersistAsync(closed with { Status = CodingRunStatus.Implementing, FixAttempts = 0 },
            new StepDone(Key, stepNumber));
    }

    // A draft that follows a failed build, test or review is a fix attempt; the first draft of a step is not.
    private static int NextFixAttempts(CodingRunState current)
        => current.Status is CodingRunStatus.BuildFailed or CodingRunStatus.TestsFailed or CodingRunStatus.ReviewRejected ? current.FixAttempts + 1 : current.FixAttempts;

    private static int CurrentStep(CodingRunState current)
    {
        var total = current.Plan?.Steps.Count ?? 0;
        return total == 0 ? 0 : Math.Min(current.StepsCompleted + 1, total);
    }

    private void EnsureEditing(CodingRunState current, int stepNumber)
    {
        if (current.WorkspaceSolutionPath is null || current.Plan is null)
        {
            throw new InvalidOperationException("Edit a step after the workspace is prepared.");
        }

        var expected = CurrentStep(current);
        if (stepNumber != expected)
        {
            throw new InvalidOperationException($"Step {stepNumber} is not the current step ({expected}).");
        }
    }

    private static bool IsClosed(CodingRunStatus status)
        => status is CodingRunStatus.Stopped or CodingRunStatus.Finished or CodingRunStatus.Failed or CodingRunStatus.NeedsHuman;

    private void EnsureOpen()
    {
        if (IsClosed(Current.Status))
        {
            throw new InvalidOperationException("This run is closed.");
        }
    }

    private async Task PersistAsync(CodingRunState next, Signal changed)
    {
        state.State = next;
        await state.WriteStateAsync();
        await PublishAsync(changed);
    }
}
