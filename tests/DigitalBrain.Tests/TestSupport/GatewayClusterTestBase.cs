using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.TestSupport;

public abstract class GatewayClusterTestBase : NeuronTestBase
{
    private HomeFeedBus? homeFeedBus;

    protected HomeFeedBus HomeFeedBus => homeFeedBus ??=
        ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

    protected GatewayService NewGatewayService(IConfiguration? configuration = null) =>
        new(
            Cluster.GrainFactory,
            configuration ?? new ConfigurationBuilder().Build(),
            HomeFeedBus,
            new SignalEgressBus(),
            new FakeHostEnvironment(),
            NullLogger<GatewayService>.Instance);

    protected static TestServerCallContext TestContext(CancellationToken cancellationToken = default) =>
        TestServerCallContext.Create(cancellationToken);
}
