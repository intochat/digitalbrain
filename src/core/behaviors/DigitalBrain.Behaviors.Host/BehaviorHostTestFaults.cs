using System.Collections.Concurrent;

namespace DigitalBrain.Behaviors.Host;

// Armed per artifact hash, not globally. Suites run in parallel and several of them deploy, so a
// one-shot global arm is consumed by whichever unrelated deploy reaches an engine first.
internal static class BehaviorHostTestFaults
{
    private static readonly ConcurrentDictionary<string, string> RefusedDeploys =
        new(StringComparer.Ordinal);

    public static void RefuseDeployOf(string artifactHash, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        RefusedDeploys[artifactHash] = reason;
    }

    internal static void ThrowIfArmed(string artifactHash)
    {
        if (RefusedDeploys.TryRemove(artifactHash, out var reason))
        {
            throw new BehaviorHostException(reason);
        }
    }
}
