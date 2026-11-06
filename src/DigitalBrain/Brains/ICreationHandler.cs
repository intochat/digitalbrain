using DigitalBrain.Brains;
using DigitalBrain.Signals;

namespace DigitalBrain.Brains;

/// <summary>
/// Runs when a brain is created and is awaited by <see cref="IBrain.Create"/>, so a new brain is complete when
/// its creator gets it. Idempotent and independent: one failing does not fail the brain.
/// </summary>
public interface ICreationHandler
{
    /// <summary>Invoked after the brain record has been persisted.</summary>
    Task Created(string brainId, Created created, CancellationToken cancellationToken);
}
