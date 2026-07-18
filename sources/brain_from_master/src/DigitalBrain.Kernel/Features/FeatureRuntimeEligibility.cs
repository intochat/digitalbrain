using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Features;

internal static class FeatureRuntimeEligibility
{
    public static async Task<bool> IsExecutableAsync(
        IFeatureInstallationGrain installation,
        FeatureCapabilityProjection projection)
    {
        try
        {
            var runtime = await installation.ReadAsync();
            var reservation = await installation.ReadReservationAsync();
            return reservation is null && runtime.InstallationId == projection.InstallationId &&
                   runtime.ActiveRelease == projection.Release && !runtime.Paused &&
                   runtime.UnconfirmedReleaseSwitch is null;
        }
        catch (FeatureCommandRejectedException exception)
            when (exception.Reason == FeatureCommandRejectionReason.Precondition)
        {
            return false;
        }
    }
}
