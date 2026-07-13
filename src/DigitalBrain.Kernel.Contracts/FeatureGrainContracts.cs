using System.Text;

namespace DigitalBrain.Kernel.Contracts;

public static class FeatureGrainIds
{
    public static string Hub(BrainOwnerId ownerId) => $"v3/{Segment(ownerId.Value)}/features";

    public static string Installation(BrainOwnerId ownerId, FeatureInstallationId installationId) =>
        $"{Hub(ownerId)}/{Segment(installationId.Value)}";

    private static string Segment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
