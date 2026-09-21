using DigitalBrain.Core;
using DigitalBrain.Testing.E2E;
using DigitalBrain.Testing.Integration;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class E2EBoundaryFacts
{
    [Fact]
    public void BrowserOptionsStayOnTheBuilderAndFailBeforeLaunch()
    {
        var headed = E2ETest.For<E2EBoundaryFacts>().ConfigureModule<CompositionFacts.ExampleModule>(m =>
            BrowserConfiguration.Configure(b => b.Headed().SlowMo(75)));
        Assert.False(headed.BrowserOptions.Headless);
        Assert.Equal(75, headed.BrowserOptions.SlowMoMilliseconds);
        Assert.DoesNotContain("Headless", headed.SerializeOverrides());
        Assert.DoesNotContain("SlowMo", headed.SerializeOverrides());
        Assert.Throws<InvalidOperationException>(() => BrowserConfiguration.Configure(_ => { }));
        var resolved = BrowserOptionsResolver.Resolve(new() { Headless = false }, false, false);
        Assert.False(resolved.Headless);
        Assert.Equal(250, resolved.SlowMoMilliseconds);
        Assert.True(BrowserOptionsResolver.Resolve(new(), false, false).Headless);
        Assert.False(BrowserOptionsResolver.Resolve(new(), true, false).Headless);
        Assert.Throws<ArgumentOutOfRangeException>(() => BrowserOptionsResolver.Resolve(new() { SlowMoMilliseconds = -1 }, false, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => BrowserOptionsResolver.Resolve(new() { StartupTimeout = TimeSpan.Zero }, false, false));
    }

    [Fact]
    public async Task InvalidBrowserOptionsFailBeforeAcquiringResources()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => E2ETest.For<E2EBoundaryFacts>()
            .WithBrowser(new() { SlowMoMilliseconds = -1 }).StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailedStartupReleasesTheHost()
    {
        foreach (var error in new Exception[] { new InvalidOperationException("launch failed"), new OperationCanceledException() })
        {
            var host = new OwnedHost();
            Assert.Same(error, await Record.ExceptionAsync(() => E2ETest.ReadyAsync(host, _ => Task.FromException(error))));
            Assert.Equal(1, host.DisposeCount);
        }
    }

    [Fact]
    public void MissingBundleFailsBeforeInfrastructureStartup()
        => Assert.Throws<InvalidOperationException>(() => ModuleBundle.Validate(Path.GetTempPath(),
            [new ModuleDefinition(typeof(CompositionFacts.ExampleModule))]));

    private sealed class OwnedHost : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }
}
