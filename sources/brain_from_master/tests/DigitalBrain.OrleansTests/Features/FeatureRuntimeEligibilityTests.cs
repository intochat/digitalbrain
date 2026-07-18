using System.Reflection;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Features;

namespace DigitalBrain.OrleansTests.Features;

public sealed class FeatureRuntimeEligibilityTests
{
    private static readonly BrainOwnerId Owner = new("owner-runtime-eligibility");
    private static readonly ActorId Actor = new("actor-runtime-eligibility");
    private static readonly FeatureInstallationId Installation = new("installation-runtime-eligibility");
    private static readonly ReleaseDigest Release = new(new string('a', 64));

    [Fact]
    public async Task Infrastructure_failure_propagates_instead_of_looking_like_an_ineligible_runtime()
    {
        var installation = DispatchProxy.Create<IFeatureInstallationGrain, RuntimeProxy>();
        ((RuntimeProxy)(object)installation).ReadFailure = new TimeoutException("storage deadline");

        await Assert.ThrowsAsync<TimeoutException>(() =>
            FeatureRuntimeEligibility.IsExecutableAsync(installation, Projection()));
    }

    [Fact]
    public async Task Missing_runtime_is_ineligible_without_failing_the_owner_catalog()
    {
        var installation = DispatchProxy.Create<IFeatureInstallationGrain, RuntimeProxy>();
        ((RuntimeProxy)(object)installation).ReadFailure =
            new FeatureCommandRejectedException(FeatureCommandRejectionReason.Precondition);

        Assert.False(await FeatureRuntimeEligibility.IsExecutableAsync(installation, Projection()));
    }

    private static FeatureCapabilityProjection Projection() => new(
        Owner,
        Installation,
        Actor,
        Release,
        new GrantRevision(1),
        "Run a bounded Feature",
        [],
        [],
        "manual",
        1,
        new string('b', 64),
        new string('c', 64));

    public class RuntimeProxy : DispatchProxy
    {
        public Exception ReadFailure { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IFeatureInstallationGrain.ReadAsync))
                return Task.FromException<FeatureInstallationSnapshot>(ReadFailure);
            if (targetMethod?.Name == nameof(IFeatureInstallationGrain.ReadReservationAsync))
                return Task.FromResult<FeatureRuntimeReservationSnapshot?>(null);
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
