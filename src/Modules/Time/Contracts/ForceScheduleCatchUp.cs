using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Messaging;
namespace DigitalBrain.Time;

// Verification/ops: backdate NextDue by MissedPeriods and run one phase-preserving catch-up.
// Same math as silo downtime; used when cluster restart is not available in-session.
[GenerateSerializer]
[Alias("time.force-schedule-catch-up")]
public sealed record ForceScheduleCatchUp(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] int MissedPeriods = 4) : RequestSynapse<ScheduleTick>;

