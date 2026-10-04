using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Testing.Module;
using Xunit;

namespace DigitalBrain.Kernel.Tests.Unit.Brain;

public sealed class BrainFacts
{
    [Fact]
    public async Task EstablishIsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await ModuleTest.Create().StartAsync(ct);
        var neuron = host.Get<IBrain>(BrainScope.Create("acct", "personal").Id);
        var first = await neuron.Establish(new("personal", "acct"));
        var second = await neuron.Establish(new("personal", "acct"));
        Assert.Equal("personal", first.Name);
        Assert.Equal("acct", first.OwnerAccountId);
        Assert.Equal(first.EstablishedAt, second.EstablishedAt);
    }

    // Activated arrives during the grain's own activation, before any per-activation watch can
    // re-attach, so these facts listen where the operating system does: the silo's signal hub.
    [Fact]
    public async Task AnEstablishedBrainPublishesActivatedOnBirthAndOnEveryLaterWake()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await ModuleTest.Create().StartAsync(ct);
        using var activations = host.SiloServices.GetRequiredService<LocalSignalHub>().Subscribe<Activated>();
        var scope = BrainScope.Create("acct", "waking").Id;
        var neuron = host.Get<IBrain>(scope);
        await neuron.Establish(new("waking", "acct"));
        var born = await activations.Reader.ReadAsync(ct);
        Assert.Equal((scope, "acct", "waking"), (born.BrainId, born.OwnerAccountId, born.Name));
        await host.DeactivateAsync(neuron, ct);
        await neuron.Read();
        Assert.Equal(scope, (await activations.Reader.ReadAsync(ct)).BrainId);
    }

    [Fact]
    public async Task AnUnestablishedBrainStaysSilentOnActivation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await ModuleTest.Create().StartAsync(ct);
        using var activations = host.SiloServices.GetRequiredService<LocalSignalHub>().Subscribe<Activated>();
        await host.Get<IBrain>(BrainScope.Create("acct", "silent").Id).Read();
        using var quiet = CancellationTokenSource.CreateLinkedTokenSource(ct);
        quiet.CancelAfter(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await activations.Reader.ReadAsync(quiet.Token));
    }
}
