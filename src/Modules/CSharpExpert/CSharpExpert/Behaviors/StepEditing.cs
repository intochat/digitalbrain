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

        edits = EditNormalizer.ResolvePaths(edits, Path.GetDirectoryName(RunWorkspace.SolutionPath(snapshot))!);
        var check = await roslyn.CheckEdits(edits, cancellationToken).ConfigureAwait(false);
        if (check.HasErrors)
        {
            var hint = await SymbolHintAsync(roslyn, edits, check.FailingEdit, cancellationToken).ConfigureAwait(false);
            await run.RecordBrokenEdit(step.Number, check.Diff, Problems(check.Diagnostics, check.FailingEdit, check.Detail + hint)).ConfigureAwait(false);
            return;
        }

        var commit = await roslyn.CommitEdits(edits, cancellationToken).ConfigureAwait(false);
        if (commit.HasErrors)
        {
            await run.RecordBrokenEdit(step.Number, commit.Diff, Problems(commit.Diagnostics, null, commit.Detail)).ConfigureAwait(false);
            return;
        }

        await run.RecordStepDrafted(step.Number, commit.Diff).ConfigureAwait(false);
    }

    private static IReadOnlyList<DiagnosticGroup> Problems(IReadOnlyList<DiagnosticHit> diagnostics, int? failingEdit, string? detail)
    {
        var groups = DiagnosticGroups.From(diagnostics);
        if (detail is not { Length: > 0 })
        {
            return groups;
        }

        var editLabel = failingEdit is { } index ? $"edit {index + 1}" : "edits";
        return [new DiagnosticGroup("edit", editLabel, [detail]), .. groups];
    }

    private static async Task<string> SymbolHintAsync(IRoslyn roslyn, IReadOnlyList<EditRequest> edits, int? failingEdit, CancellationToken cancellationToken)
    {
        if (failingEdit is not { } index || index < 0 || index >= edits.Count || edits[index].SymbolId is not { } symbolId)
        {
            return string.Empty;
        }

        var simpleName = symbolId[(symbolId.IndexOf(':', StringComparison.Ordinal) + 1)..].Split('(')[0].Split('.')[^1];
        var matches = await roslyn.FindSymbols(new SymbolSearch(simpleName, Limit: 5), cancellationToken).ConfigureAwait(false);
        return matches.Items.Count == 0
            ? string.Empty
            : $" Existing symbols named '{simpleName}': {string.Join(", ", matches.Items.Select(hit => hit.Id))}";
    }
}
