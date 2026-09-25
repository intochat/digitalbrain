using DigitalBrain.Contracts;
using DigitalBrain.Microsoft.DotNet;
using DigitalBrain.Microsoft.Roslyn;
using Orleans.Concurrency;

namespace DigitalBrain.CSharpExpert;

[Alias("csharp-expert.run")]
public interface ICodingRun : INeuron
{
    Task Request(FeatureRequest request);

    Task Clarify(string clarification);

    Task Approve();

    Task Stop();

    Task RecordContext(ProjectModel model);

    Task RecordPlan(CodingPlan plan);

    Task PrepareWorkspace(string workspaceRoot, string workspaceSolutionPath);

    Task RecordStepDrafted(int stepNumber, string diff);

    Task RecordBrokenEdit(int stepNumber, string diff, IReadOnlyList<DiagnosticGroup> diagnostics);

    Task RecordBuild(BuildOutcome outcome);

    Task RecordTests(TestOutcome outcome);

    Task RecordReview(IReadOnlyList<string> findings);

    Task RecordNeedsHuman(string reason);

    Task Fail(string reason);

    [ReadOnly]
    Task<CodingRunSnapshot> Read();
}
