using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Flutter;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Testing.Module;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests;

// One in-process brain with the Flutter module's HTTP edge. Facts isolate in their own
// workspace: a fresh brain name per fact, with the caller the authenticated edge would stamp
// for it, so routes, neuron keys and signals all land in that fact's scope.
public sealed class FlutterHostFixture : IAsyncLifetime
{
    private ModuleBrain? _brain;

    public ModuleBrain Brain => _brain ?? throw new InvalidOperationException("The Flutter host has not started yet.");

    public async ValueTask InitializeAsync()
        => _brain = await ModuleTest.Create()
            .WithModule<FlutterModule, FlutterModuleOptions>(flutter => flutter.BackendOnly())
            .WithHttpEdge()
            .StartAsync();

    public async ValueTask DisposeAsync()
    {
        if (_brain is not null) { await _brain.DisposeAsync(); }
    }

    // A fresh workspace name per fact. The fact's own neuron calls run unstamped — the trusted
    // internal path, the same standing silo-side setup has — while its HTTP requests are stamped
    // by the edge itself (Open posture, brain taken from the route).
    public string Workspace()
    {
        Orleans.Runtime.RequestContext.Remove(CallerContextStamper.RequestContextKey);
        return "ws-" + Guid.NewGuid().ToString("N")[..12];
    }
}

[CollectionDefinition(Name)]
public sealed class FlutterHostCollection : ICollectionFixture<FlutterHostFixture>
{
    public const string Name = "flutter-http-host";
}
