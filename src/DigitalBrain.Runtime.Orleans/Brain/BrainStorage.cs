using DigitalBrain.Brains;
using DigitalBrain.State;

namespace DigitalBrain.Runtime.Orleans.Brain;

/// <summary>
/// What a brain persists: whose it is and when it came to be.
/// </summary>
[ExportableState]
public sealed record BrainStorage
{
    public Owner? Owner { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
