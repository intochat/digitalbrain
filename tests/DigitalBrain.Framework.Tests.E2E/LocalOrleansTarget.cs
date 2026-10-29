using DigitalBrain.Runtime.Orleans;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Spi;
using DigitalBrain.Testing;

namespace DigitalBrain.Framework.Tests.E2E;

/// <summary>The local topology (LocalProfile on a temp Home: memory streams, file PubSubStore, BrainLog cluster store).</summary>
public sealed class LocalOrleansTarget : OrleansFrameworkTarget
{
    public const string TargetName = "local-orleans";

    public override string Name => TargetName;

    public override Task<IInProcessHost> Start(CancellationToken ct) => SiloHost.Local(ct: ct);

    public override async Task<RunningTarget> StartHost(FrameworkHostOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        var clock = new OffsetClock();
        var factory = new LocalSiloHostFactory(new BrainStorageRegistration(typeof(FrameworkModule).Assembly), OrleansRun.ClockServices(clock));
        var host = await factory.Start(new InProcessHostOptions { Modules = [typeof(FrameworkModule)], Configuration = OrleansRun.Configuration(options) }, ct);
        return OrleansRun.Started(host, options, clock);
    }
}
