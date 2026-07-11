namespace DigitalBrain.Core.Runtime;

public sealed record TopologyResource(string Name, string Kind, bool Required, string Profile, string ImageDigest);
public sealed record TopologySnapshot(IReadOnlyList<TopologyResource> Resources, string Profile);
public sealed record TopologyDrift(string Resource, string Difference, bool Blocking);
public sealed record DeploymentPreview(IReadOnlyList<TopologyResource> Desired, IReadOnlyList<TopologyDrift> Drift, bool CanApply);

/// <summary>Pure, non-mutating deployment preview and drift policy for runtime topology.</summary>
public static class DeploymentPreviewer
{
    public static DeploymentPreview Preview(TopologySnapshot desired, TopologySnapshot? actual)
    {
        var drift = new List<TopologyDrift>();
        if (actual is null)
            drift.Add(new TopologyDrift("topology", "actual topology unavailable", true));
        else
        {
            if (!string.Equals(desired.Profile, actual.Profile, StringComparison.OrdinalIgnoreCase))
                drift.Add(new TopologyDrift("profile", $"expected {desired.Profile}, found {actual.Profile}", true));
            var actualByName = actual.Resources.ToDictionary(x => x.Name, StringComparer.Ordinal);
            foreach (var resource in desired.Resources)
            {
                if (!actualByName.TryGetValue(resource.Name, out var found))
                {
                    drift.Add(new TopologyDrift(resource.Name, "missing", resource.Required));
                    continue;
                }
                if (!string.Equals(resource.Kind, found.Kind, StringComparison.Ordinal) || !string.Equals(resource.ImageDigest, found.ImageDigest, StringComparison.Ordinal))
                    drift.Add(new TopologyDrift(resource.Name, "kind or immutable image digest differs", resource.Required));
            }
        }
        return new DeploymentPreview(desired.Resources, drift, drift.All(x => !x.Blocking));
    }
}
