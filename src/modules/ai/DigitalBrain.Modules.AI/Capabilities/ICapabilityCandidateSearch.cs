using DigitalBrain.Abstractions;

namespace DigitalBrain.AI;

public interface ICapabilityCandidateSearch
{
    Task<IReadOnlyList<CapabilityCandidate>> SearchAsync(
        OwnerId owner,
        string prompt,
        int limit,
        CancellationToken cancellationToken);
}
