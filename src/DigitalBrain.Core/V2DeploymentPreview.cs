namespace DigitalBrain.Core.V2;

public sealed record V2TopologyResource(string Name, string Kind, bool Required, string Profile, string ImageDigest);
public sealed record V2TopologySnapshot(IReadOnlyList<V2TopologyResource> Resources, string Profile);
public sealed record V2TopologyDrift(string Resource, string Difference, bool Blocking);
public sealed record V2DeploymentPreview(IReadOnlyList<V2TopologyResource> Desired, IReadOnlyList<V2TopologyDrift> Drift, bool CanApply);

/// <summary>Pure, non-mutating deployment preview and drift policy for V2 topology.</summary>
public static class V2DeploymentPreviewer
{
    public static V2DeploymentPreview Preview(V2TopologySnapshot desired, V2TopologySnapshot? actual)
    {
        var drift = new List<V2TopologyDrift>();
        if (actual is null)
            drift.Add(new V2TopologyDrift("topology", "actual topology unavailable", true));
        else
        {
            if (!string.Equals(desired.Profile, actual.Profile, StringComparison.OrdinalIgnoreCase))
                drift.Add(new V2TopologyDrift("profile", $"expected {desired.Profile}, found {actual.Profile}", true));
            var actualByName = actual.Resources.ToDictionary(x => x.Name, StringComparer.Ordinal);
            foreach (var resource in desired.Resources)
            {
                if (!actualByName.TryGetValue(resource.Name, out var found))
                {
                    drift.Add(new V2TopologyDrift(resource.Name, "missing", resource.Required));
                    continue;
                }
                if (!string.Equals(resource.Kind, found.Kind, StringComparison.Ordinal) || !string.Equals(resource.ImageDigest, found.ImageDigest, StringComparison.Ordinal))
                    drift.Add(new V2TopologyDrift(resource.Name, "kind or immutable image digest differs", resource.Required));
            }
        }
        return new V2DeploymentPreview(desired.Resources, drift, drift.All(x => !x.Blocking));
    }
}
