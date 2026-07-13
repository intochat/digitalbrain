using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Features;

internal static class FeatureStateEquality
{
    public static bool Same(FeatureInstallationState? left, FeatureInstallationState? right) =>
        ReferenceEquals(left, right) ||
        left is not null &&
        right is not null &&
        left.InstallationId == right.InstallationId &&
        left.ActiveRelease == right.ActiveRelease &&
        left.PreviousRelease == right.PreviousRelease &&
        string.Equals(left.StateJson, right.StateJson, StringComparison.Ordinal) &&
        left.Paused == right.Paused &&
        left.Inbox.SequenceEqual(right.Inbox) &&
        Equals(left.Lease, right.Lease) &&
        left.Completions.SequenceEqual(right.Completions) &&
        left.Intents.SequenceEqual(right.Intents) &&
        left.NextFence == right.NextFence &&
        left.Revision == right.Revision &&
        string.Equals(left.PauseReason, right.PauseReason, StringComparison.Ordinal) &&
        left.Schedules.SequenceEqual(right.Schedules);

    public static bool Same(FeatureHubState? left, FeatureHubState? right) =>
        ReferenceEquals(left, right) ||
        left is not null &&
        right is not null &&
        left.Revision == right.Revision &&
        Same(left.Installations, right.Installations) &&
        Same(left.FanOuts, right.FanOuts);

    private static bool Same(
        IReadOnlyList<FeatureInstallationRegistration> left,
        IReadOnlyList<FeatureInstallationRegistration> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.InstallationId == pair.Second.InstallationId &&
            pair.First.Release == pair.Second.Release &&
            pair.First.Subscriptions.SequenceEqual(pair.Second.Subscriptions, StringComparer.Ordinal));

    private static bool Same(
        IReadOnlyList<FeatureFanOutState> left,
        IReadOnlyList<FeatureFanOutState> right) =>
        left.Count == right.Count && left.Zip(right).All(pair =>
            pair.First.Input == pair.Second.Input &&
            pair.First.Deliveries.SequenceEqual(pair.Second.Deliveries));
}
