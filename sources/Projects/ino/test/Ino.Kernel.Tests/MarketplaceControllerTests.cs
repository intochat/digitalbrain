using System.Text.Json;
using Ino.Aspire.Hosting;
using Ino.Core;
using Ino.Core.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Orleans;
using Xunit;

namespace Ino.Kernel.Tests;

public class MarketplaceControllerTests : IDisposable
{
    private readonly string _feedPath = Path.Combine(Path.GetTempPath(), $"mkt-feed-{Guid.NewGuid()}.json");
    private readonly string _installedPath = Path.Combine(Path.GetTempPath(), $"mkt-installed-{Guid.NewGuid()}.json");

    public void Dispose()
    {
        if (File.Exists(_feedPath)) File.Delete(_feedPath);
        if (File.Exists(_installedPath)) File.Delete(_installedPath);
    }

    private MarketplaceController MakeController(
        FakeDomainRestartService? restart = null,
        IGrainFactory? grains = null)
    {
        var options = Options.Create(new MarketplaceControllerOptions
        {
            MarketplaceFeedPath = _feedPath,
            InstalledStatePath = _installedPath,
            RestartTimeout = TimeSpan.FromSeconds(5),
        });
        return new MarketplaceController(
            options,
            restart ?? new FakeDomainRestartService(),
            grains ?? Substitute.For<IGrainFactory>(),
            NullLogger<MarketplaceController>.Instance);
    }

    private void WriteFeed(params string[] domainIds)
    {
        var feed = new MarketplaceFeed(
            domainIds.Select(id => new MarketplaceFeedEntry(
                DomainId.From(id), "desc", "1.0.0",
                Array.Empty<MarketplaceNeuronMetadata>())).ToArray());
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new DomainIdJsonConverter(), new NeuronIdJsonConverter() },
        };
        File.WriteAllText(_feedPath, JsonSerializer.Serialize(feed, jsonOptions));
    }

    [Fact]
    public async Task Install_returns_404_for_unknown_id()
    {
        WriteFeed("Ino.Testing.Fixture.Alpha");
        var controller = MakeController();

        var result = await controller.Install("Ino.Testing.Fixture.Unknown", TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Install_returns_409_when_already_installed()
    {
        WriteFeed("Ino.Testing.Fixture.Alpha");
        InstalledSet.Save(new HashSet<DomainId> { DomainId.From("Ino.Testing.Fixture.Alpha") }, _installedPath);
        var controller = MakeController();

        var result = await controller.Install("Ino.Testing.Fixture.Alpha", TestContext.Current.CancellationToken);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Install_calls_restart_service_on_success()
    {
        WriteFeed("Ino.Testing.Fixture.Alpha");
        var restart = new FakeDomainRestartService();
        var controller = MakeController(restart);

        var result = await controller.Install("Ino.Testing.Fixture.Alpha", TestContext.Current.CancellationToken);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, restart.CallCount);
    }

    [Fact]
    public async Task Install_returns_pending_restart_status_when_restart_is_a_no_op()
    {
        WriteFeed("Ino.Testing.Fixture.Alpha");
        var restart = new FakeDomainRestartService { Outcome = RestartOutcome.PendingRestart };
        var controller = MakeController(restart);

        var result = await controller.Install("Ino.Testing.Fixture.Alpha", TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"status\":\"installed_pending_restart\"", json);
    }

    [Fact]
    public async Task Uninstall_returns_pending_restart_status_when_restart_is_a_no_op()
    {
        WriteFeed("Ino.Testing.Fixture.Alpha");
        InstalledSet.Save(new HashSet<DomainId> { DomainId.From("Ino.Testing.Fixture.Alpha") }, _installedPath);
        var restart = new FakeDomainRestartService { Outcome = RestartOutcome.PendingRestart };
        var controller = MakeController(restart);

        var result = await controller.Uninstall("Ino.Testing.Fixture.Alpha", TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"status\":\"uninstalled_pending_restart\"", json);
    }

    [Fact]
    public async Task Install_returns_504_when_restart_fails()
    {
        WriteFeed("Ino.Testing.Fixture.Alpha");
        var restart = new FakeDomainRestartService
        {
            NextError = new TimeoutException("silo failed to start"),
        };
        var controller = MakeController(restart);

        var result = await controller.Install("Ino.Testing.Fixture.Alpha", TestContext.Current.CancellationToken);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(504, obj.StatusCode);
    }

    [Fact]
    public async Task Install_reverts_installed_state_when_restart_fails()
    {
        WriteFeed("Ino.Testing.Fixture.Alpha");
        var restart = new FakeDomainRestartService
        {
            NextError = new TimeoutException("silo failed to start"),
        };
        var controller = MakeController(restart);

        var result = await controller.Install("Ino.Testing.Fixture.Alpha", TestContext.Current.CancellationToken);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(504, obj.StatusCode);

        var persisted = InstalledSet.Load(_installedPath);
        // restart failure must not leave installed.json showing a domain the runtime did not pick up.
        Assert.DoesNotContain(DomainId.From("Ino.Testing.Fixture.Alpha"), persisted);
    }

    [Fact]
    public async Task Uninstall_reverts_installed_state_when_restart_fails()
    {
        WriteFeed("Ino.Testing.Fixture.Alpha");
        InstalledSet.Save(new HashSet<DomainId> { DomainId.From("Ino.Testing.Fixture.Alpha") }, _installedPath);
        var restart = new FakeDomainRestartService
        {
            NextError = new TimeoutException("silo failed to start"),
        };
        var controller = MakeController(restart);

        var result = await controller.Uninstall("Ino.Testing.Fixture.Alpha", TestContext.Current.CancellationToken);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(504, obj.StatusCode);

        var persisted = InstalledSet.Load(_installedPath);
        // restart failure on uninstall must put the domain back so retry sees consistent state.
        Assert.Contains(DomainId.From("Ino.Testing.Fixture.Alpha"), persisted);
    }

    [Fact]
    public void Consent_returns_501()
    {
        var controller = MakeController();

        var result = controller.Consent("anything");

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(501, obj.StatusCode);
    }

    [Fact]
    public async Task Uninstall_returns_404_when_not_installed()
    {
        var controller = MakeController();

        var result = await controller.Uninstall("Ino.Testing.Fixture.Missing", TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetInstalledNeurons_returns_404_when_domain_not_installed()
    {
        var controller = MakeController();

        var result = await controller.GetInstalledNeurons(
            "Ino.Testing.Fixture.Missing", TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetInstalledNeurons_returns_scoped_neurons_for_installed_domain()
    {
        var travelId = DomainId.From("Ino.Domains.Travel");
        var taxiId = DomainId.From("Ino.Domains.Taxi");
        InstalledSet.Save(new HashSet<DomainId> { travelId }, _installedPath);

        var travelNeuron = new NeuronDefinition(
            NeuronId.From("travel.plan-trip"),
            "Plan a trip",
            "desc",
            typeof(TravelSynapse),
            ["plan"]);
        var taxiNeuron = new NeuronDefinition(
            NeuronId.From("taxi.find-ride"),
            "Find a ride",
            "desc",
            typeof(TaxiSynapse),
            ["ride"]);

        // Discovery returns BOTH domains' registrations; the controller must
        // scope down to the Travel synapse type and filter out Taxi.
        var dump = new DiscoveryDump(
            Canonical:
            [
                new CanonicalTarget(typeof(TravelSynapse), typeof(object), travelId, []),
                new CanonicalTarget(typeof(TaxiSynapse), typeof(object), taxiId, []),
            ],
            Reactive: [],
            CountsBySilo: new Dictionary<string, int>());

        var discovery = Substitute.For<IDiscovery>();
        discovery.DumpAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(dump));
        discovery.DumpNeuronsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<INeuronDefinition>>(
                [travelNeuron, taxiNeuron]));

        var grains = Substitute.For<IGrainFactory>();
        grains.GetGrain<IDiscovery>(0, null).Returns(discovery);

        var controller = MakeController(grains: grains);

        var result = await controller.GetInstalledNeurons(
            travelId.Value, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"domainId\":\"Ino.Domains.Travel\"", json);
        Assert.Contains("\"id\":\"travel.plan-trip\"", json);
        // the endpoint must scope neurons to the requested domain only
        Assert.DoesNotContain("taxi.find-ride", json);
    }

    private sealed record TravelSynapse : ISynapse;
    private sealed record TaxiSynapse : ISynapse;
}
