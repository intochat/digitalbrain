using DigitalBrain.Apps;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Enforcement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

namespace DigitalBrain.OS;

// The operating system's first-party apps: package ids ("owner/name") that every brain carries,
// installed at the published revision when the brain activates and upgraded when a new revision
// ships. The list is deployment configuration; a distribution names its own.
public sealed class OsAppsOptions
{
    public IList<string> Apps { get; } = [];
}

public static class OsAppsHosting
{
    public static IServiceCollection AddOperatingSystemBoot(this IServiceCollection services, Action<OsAppsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure is not null) { services.Configure(configure); }
        services.AddHostedService<OsBootService>();
        return services;
    }
}

// The OS boots in the system's own language: the brain neuron publishes Activated, and this
// behavior reacts by ensuring the OS apps are installed and current — through IApp and IPackage,
// the same contracts any app uses. Delivery is at-least-once and Activated repeats on every
// activation, so every step reads before it writes; a package that has not shipped yet is
// simply picked up on the next activation.
internal sealed class OsBootService(
    LocalSignalHub signals, IGrainFactory grains, IOptions<OsAppsOptions> options, ILogger<OsBootService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Apps.Count == 0) { return; }
        var apps = options.Value.Apps.Select(PackageId.Parse).ToArray();
        using var activations = signals.Subscribe<Activated>();
        await foreach (var activated in activations.Reader.ReadAllAsync(stoppingToken))
        {
            foreach (var package in apps)
            {
                try { await EnsureCurrentAsync(activated, package, stoppingToken); }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // One app failing must not keep Settings from starting; the next
                    // activation retries.
                    logger.LogWarning(error, "OS app {Package} did not start in brain {Brain}.", package, activated.BrainId);
                }
            }
        }
    }

    private async Task EnsureCurrentAsync(Activated activated, PackageId package, CancellationToken cancellationToken)
    {
        // The brain's (owner, name) pair is the scope identity the kernel derives everywhere
        // from the caller; the install must land in the activated brain's own scope.
        CallerContextStamper.Stamp(new CallerContext
        {
            PrincipalId = "os",
            AccountId = activated.OwnerAccountId,
            BrainId = activated.Name,
            Kind = CallerKind.Platform,
            StampedBy = TrustedEdge.Platform,
        });
        var published = (await grains.GetGrain<IPackage>(package.ToString()).Read().WaitAsync(cancellationToken)).Published;
        if (published is null) { return; }
        var app = grains.GetGrain<IApp>(activated.BrainId + "/packages/" + package);
        var snapshot = await app.Read().WaitAsync(cancellationToken);
        if (snapshot.Status != AppStatus.Installed)
        {
            await app.Install(new(Guid.NewGuid(), new(package, published), new Dictionary<string, string>())).WaitAsync(cancellationToken);
        }
        else if (snapshot.Revision is { } installed && installed.Revision != published)
        {
            await app.Upgrade(new(Guid.NewGuid(), new(package, published))).WaitAsync(cancellationToken);
        }
    }
}
