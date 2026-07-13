using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Features;

public static class FeatureHubTransitions
{
    public static FeatureHubState Register(
        FeatureHubState state,
        FeatureInstallationRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(registration);
        if (string.IsNullOrWhiteSpace(registration.InstallationId.Value) ||
            string.IsNullOrWhiteSpace(registration.Release.Value))
            throw new ArgumentException("A complete feature installation registration is required.", nameof(registration));
        ArgumentNullException.ThrowIfNull(registration.Subscriptions);
        if (registration.Subscriptions.Length == 0 ||
            registration.Subscriptions.Any(subscription =>
                string.IsNullOrWhiteSpace(subscription) ||
                subscription.Length > 256 ||
                subscription.Any(char.IsControl) ||
                !string.Equals(subscription, subscription.Trim(), StringComparison.Ordinal)) ||
            registration.Subscriptions.Distinct(StringComparer.Ordinal).Count() != registration.Subscriptions.Length)
            throw new ArgumentException("Canonical unique feature subscriptions are required.", nameof(registration));

        var existing = Array.FindIndex(
            state.Installations,
            candidate => candidate.InstallationId == registration.InstallationId);
        if (existing >= 0)
        {
            var replaced = state.Installations.ToArray();
            replaced[existing] = registration;
            return state with { Installations = replaced, Revision = checked(state.Revision + 1) };
        }

        if (state.Installations.Length >= FeatureLimits.InstallationsPerOwner)
            throw new FeatureLimitExceededException("An owner can have at most 100 feature installations.");

        return state with
        {
            Installations = [.. state.Installations, registration],
            Revision = checked(state.Revision + 1)
        };
    }

    public static FeatureHubState BeginFanOut(FeatureHubState state, FeatureInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        FeatureInstallationTransitions.ValidateInput(input);
        var existing = state.FanOuts.FirstOrDefault(batch =>
            string.Equals(batch.Input.InputId, input.InputId, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (!string.Equals(
                FeatureInstallationTransitions.InputDigest(existing.Input),
                FeatureInstallationTransitions.InputDigest(input),
                StringComparison.Ordinal))
                throw new FeatureConcurrencyException("The fan-out input id is already bound to different content.");
            return state;
        }

        var deliveries = state.Installations
            .Where(registration => registration.Subscriptions.Any(
                subscription => string.Equals(subscription, input.Kind, StringComparison.Ordinal)))
            .Select(registration => new FeatureFanOutDeliveryState(registration.InstallationId, false))
            .ToArray();
        var batch = new FeatureFanOutState(input, deliveries);
        var retained = state.FanOuts;
        if (retained.Length >= FeatureLimits.FanOutBatches)
        {
            var completedIndex = Array.FindIndex(
                retained,
                candidate => candidate.Deliveries.All(delivery => delivery.Delivered));
            if (completedIndex < 0)
                throw new FeatureLimitExceededException("Pending feature fan-out exceeds the durable ledger capacity.");
            retained = retained.Where((_, index) => index != completedIndex).ToArray();
        }
        FeatureFanOutState[] fanOuts = [.. retained, batch];
        return state with { FanOuts = fanOuts, Revision = checked(state.Revision + 1) };
    }

    public static FeatureHubState RecordDeliveries(
        FeatureHubState state,
        string inputId,
        IReadOnlySet<FeatureInstallationId> delivered)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputId);
        ArgumentNullException.ThrowIfNull(delivered);
        var index = Array.FindIndex(
            state.FanOuts,
            batch => string.Equals(batch.Input.InputId, inputId, StringComparison.Ordinal));
        if (index < 0)
            throw new KeyNotFoundException("The feature fan-out batch does not exist.");
        var batch = state.FanOuts[index];
        var deliveries = batch.Deliveries.Select(delivery =>
            delivery.Delivered || delivered.Contains(delivery.InstallationId)
                ? delivery with { Delivered = true }
                : delivery).ToArray();
        if (deliveries.SequenceEqual(batch.Deliveries))
            return state;
        var fanOuts = state.FanOuts.ToArray();
        fanOuts[index] = batch with { Deliveries = deliveries };
        return state with { FanOuts = fanOuts, Revision = checked(state.Revision + 1) };
    }
}
