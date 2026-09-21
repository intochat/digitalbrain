using DigitalBrain.Core;
using Orleans.Hosting;

namespace DigitalBrain.Tests;

public sealed class E2EBoundaryFacts
{
    [Fact]
    public void BrowserOptionsStayOnTheBuilderAndOutsideTheOverrideEnvelope()
    {
        var headed = E2ETest.For<E2EBoundaryFacts>().ConfigureModule<BoundaryModule>(_ =>
            BrowserConfiguration.Configure(browser => browser.Headed().SlowMo(75)));
        Assert.False(headed.BrowserOptions.Headless);
        Assert.Equal(75, headed.BrowserOptions.SlowMoMilliseconds);
        Assert.DoesNotContain("Headless", headed.SerializeOverrides(), StringComparison.Ordinal);
        Assert.DoesNotContain("SlowMo", headed.SerializeOverrides(), StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => BrowserConfiguration.Configure(_ => { }));
    }

    [Fact]
    public void BrowserDefaultsResolveFromExplicitChoiceThenEnvironment()
    {
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
        => await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => E2ETest.For<E2EBoundaryFacts>()
            .WithBrowser(new() { SlowMoMilliseconds = -1 }).StartAsync(TestContext.Current.CancellationToken));

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
    public async Task LocalServiceSubstitutionIsRejectedBeforeStartup()
        => await Assert.ThrowsAsync<NotSupportedException>(() => E2ETest.Create()
            .WithModule<BoundaryModule>(module => module.ConfigureLocalServices(_ => { }))
            .StartAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task AnEmptyCompositionIsRejectedBeforeStartup()
        => await Assert.ThrowsAsync<ArgumentException>(() => E2ETest.Create()
            .StartAsync(TestContext.Current.CancellationToken));

    private sealed class OwnedHost : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }
}

[ModuleConfiguration(typeof(BoundaryContract))]
public sealed class BoundaryModule : IModule
{
    public void Configure(ISiloBuilder silo) { }
}

public sealed class BoundaryOptions { public string? Label { get; set; } }

public sealed class BoundaryContract() : ModuleConfigurationContract<BoundaryModule, BoundaryOptions>("Label")
{
    protected override ModuleDefinition Compile(BoundaryOptions options)
        => new(typeof(BoundaryModule), new Dictionary<string, string?> { ["Boundary:Label"] = options.Label });
}