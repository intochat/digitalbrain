using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Testing.E2E;

namespace DigitalBrain.Tests;

public sealed class E2ECompositionFacts
{
    [Fact]
    public void BrowserOptionsRemainLocalAndBuildersAreIsolated()
    {
        var visible = E2ETest.For<E2ECompositionFacts>().ConfigureModule<FlutterModule>(f => f.RunWebApp(b => b.Headed().SlowMo(75)));
        var headless = E2ETest.For<E2ECompositionFacts>().ConfigureModule<FlutterModule>(f => f.RunWebApp());
        Assert.False(visible.BrowserOptions.Headless);
        Assert.Equal(75, visible.BrowserOptions.SlowMoMilliseconds);
        Assert.True(headless.BrowserOptions.Headless);
        Assert.Null(headless.BrowserOptions.SlowMoMilliseconds);
        Assert.DoesNotContain("Headless", visible.SerializeOverrides());
        Assert.DoesNotContain("SlowMo", visible.SerializeOverrides());
        Assert.Throws<InvalidOperationException>(() => new BrainCompositionBuilder().WithModule<FlutterModule>(f => f.RunWebApp(b => b.Headed())));
    }

    [Fact]
    public async Task FailedAndCanceledBrowserStartupReleaseTheHost()
    {
        foreach (var error in new Exception[] { new InvalidOperationException("launch failed"), new OperationCanceledException() })
        {
            var host = new OwnedHost();
            var actual = await Record.ExceptionAsync(() => E2ETest.ReadyAsync(host, _ => Task.FromException(error)));
            Assert.Same(error, actual);
            Assert.Equal(1, host.DisposeCount);
        }
    }

    private sealed class OwnedHost : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }

    [Fact]
    public void SelectedModuleDefaultsAreOverridable()
    {
        var defaults = E2ETest.Create().WithModule<FlutterModule>().BuildComposition();
        Assert.Equal("Web", defaults.Modules.Single(m => m.ModuleType == typeof(FlutterModule)).Configuration["DigitalBrain:Flutter:Hosting:Kind"]);
        var explicitBackend = E2ETest.Create().WithModule<FlutterModule>(f => f.BackendOnly()).BuildComposition();
        Assert.Equal("None", explicitBackend.Modules.Single(m => m.ModuleType == typeof(FlutterModule)).Configuration["DigitalBrain:Flutter:Hosting:Kind"]);
    }

    [Fact]
    public void ApplicationOverridesDoNotApplyModuleDefaults()
    {
        var envelope = E2ETest.For<E2ECompositionFacts>().ConfigureModule<FlutterModule>(f =>
            f.ConfigureOptions<FlutterModuleOptions>(o => o.Hosting = o.Hosting with { ShellName = "chosen" }, "Hosting.ShellName"))
            .SerializeOverrides();
        var graph = new BrainCompositionBuilder().WithModule<FlutterModule>(f => f.RunDesktopApp()).ApplyOverrides(envelope).Build();
        Assert.Equal("Window", graph.Modules.Single(m => m.ModuleType == typeof(FlutterModule)).Configuration["DigitalBrain:Flutter:Hosting:Kind"]);
        Assert.Throws<ArgumentException>(() => new BrainCompositionBuilder().ApplyOverrides(envelope));
    }
}
