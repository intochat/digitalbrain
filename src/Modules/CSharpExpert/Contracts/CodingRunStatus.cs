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
}
