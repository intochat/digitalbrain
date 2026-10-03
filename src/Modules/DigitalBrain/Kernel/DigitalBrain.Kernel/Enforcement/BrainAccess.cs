namespace DigitalBrain.Kernel.Enforcement;

// Modules that own brain-scoped HTTP routes ask this seam whether the stamped principal may
// reach a brain. Identity registers the directory-backed implementation.
public interface IBrainAccess
{
    ValueTask<bool> CanAccessAsync(string principalId, string brainId, CancellationToken cancellationToken = default);
}
