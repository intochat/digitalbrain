using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.Roslyn;

namespace DigitalBrain.CSharpExpert;

internal static class StepEditing
{
    public static async Task ApplyAsync(IDigitalBrain brain, ICodingRun run, CodingRunSnapshot snapshot, PlanStep step, string? failure, CancellationToken cancellationToken)
    {
        var request = snapshot.Request ?? throw new InvalidOperationException("Editing started before the feature request.");
        var profile = await brain.Get<ICodingProfile>(CodingWorkspace.Id(request.SolutionPath)).Read().ConfigureAwait(false);
        var agent = brain.Get<ICodingAgent>(profile.ImplementerAgentId);
        var roslyn = brain.Get<IRoslyn>(CodingWorkspace.Id(RunWorkspace.SolutionPath(snapshot)));
        var proposal = new EditProposal(snapshot.RunId, step.Number, step.Title, step.Detail, step.Files, snapshot.Model?.MapText ?? string.Empty, failure,
            StepSources.Read(Path.GetDirectoryName(snapshot.WorkspaceSolutionPath), step.Files));
        IReadOnlyList<EditRequest> edits;
        try
        {
            edits = await agent.ProposeEdits(proposal, cancellationToken).ConfigureAwait(false);
        }
        catch (FormatException unreadable)
        {
            await run.RecordBrokenEdit(step.Number, string.Empty, [new DiagnosticGroup("edit", "reply", [unreadable.Message])]).ConfigureAwait(false);
            return;
        }

        if (edits.Count == 0)
        {
            await run.RecordBrokenEdit(step.Number, string.Empty, [new DiagnosticGroup("edit", "none", ["the implementer proposed no edits for this step"])]).ConfigureAwait(false);
            return;
        }

        var check = await roslyn.CheckEdits(edits, cancellationToken).ConfigureAwait(false);
        if (check.HasErrors)
        {
            await run.RecordBrokenEdit(step.Number, check.Diff, DiagnosticGroups.From(check.Diagnostics)).ConfigureAwait(false);
            return;
        }

        var commit = await roslyn.CommitEdits(edits, cancellationToken).ConfigureAwait(false);
        if (commit.HasErrors)
        {
            await run.RecordBrokenEdit(step.Number, commit.Diff, DiagnosticGroups.From(commit.Diagnostics)).ConfigureAwait(false);
            return;
        }

        await run.RecordStepDrafted(step.Number, commit.Diff).ConfigureAwait(false);
    }
}
