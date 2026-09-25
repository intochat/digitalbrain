using DigitalBrain.Contracts;

namespace DigitalBrain.CSharpExpert;

[GenerateSerializer, Alias("csharp-expert.run-status")]
public enum CodingRunStatus
{
    Requested = 0,
    ContextReady = 1,
    PlanDrafted = 2,
    Failed = 3,
    PlanApproved = 4,
    Stopped = 5,
    WorkspaceReady = 6,
    Implementing = 7,
    StepDrafted = 8,
    BuildPassed = 9,
    BuildFailed = 10,
    TestsPassed = 11,
    TestsFailed = 12,
    NeedsHuman = 13,
    Finished = 14,
}
