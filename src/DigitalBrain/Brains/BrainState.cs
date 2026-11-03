using DigitalBrain.State;

namespace DigitalBrain.Brains;

/// <summary>What a brain persists: whose it is and when it came to be.</summary>
/// <param name="Owner">The account the brain belongs to.</param>
/// <param name="CreatedAt">When <see cref="IBrain.Create"/> succeeded.</param>
[ExportableState]
public sealed record BrainState(Owner Owner, DateTimeOffset CreatedAt);
