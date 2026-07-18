using Aspire.Hosting;
using Grpc.Net.Client;
using Ino.Core;
using Ino.NeuronTesting.Internals;
using Xunit;

namespace Ino.NeuronTesting;

// Abstract base for neuron end-to-end tests. The Aspire AppHost fixture is
// per-test-class via IClassFixture<NeuronAppHostFixture<TAppHost>> so a class
// with N [Fact]s boots Aspire ONCE, not N times. Per-method resources
// (PlaywrightLifecycle, gRPC channel) live on the test instance and reset
// between facts so the conversational state in NeuronSession can't leak across
// tests. Subclasses propagate the fixture through their own primary
// constructor — see TravelNeuronTest<TNeuron> for the per-domain pattern.
public abstract class NeuronE2ETest<TNeuron, TAppHost>(NeuronAppHostFixture<TAppHost> fixture)
    : IAsyncLifetime, IClassFixture<NeuronAppHostFixture<TAppHost>>
    where TNeuron : class
    where TAppHost : class
{
    PlaywrightLifecycle? _playwright;
    GrpcChannel? _channel;
    global::Ino.Grpc.Ino.InoClient? _client;

    protected NeuronAppHostFixture<TAppHost> Fixture => fixture;
    protected DistributedApplication App => fixture.App;
    protected string KernelGrpcUrl => fixture.KernelGrpcUrl;

    public NeuronId NeuronUnderTest { get; private set; }

    public ValueTask InitializeAsync()
    {
        // Force metadata load so NeuronIdResolver's AppDomain scan sees IDomain
        // implementations in assemblies the JIT may not have touched yet.
        _ = typeof(TNeuron).Assembly;
        NeuronUnderTest = NeuronIdResolver.Resolve(typeof(TNeuron));

        _playwright = new PlaywrightLifecycle();
        _channel = InoGrpcChannelFactory.ForKernel(fixture.KernelGrpcUrl);
        _client = new global::Ino.Grpc.Ino.InoClient(_channel);
        return ValueTask.CompletedTask;
    }

    // Opens a fresh session with a unique user id. The caller owns the session
    // lifetime — use `await using var s = Open();` so DisposeAsync is called.
    protected NeuronSession Open(string? userId = null) =>
        new(_client!, _playwright!, KernelGrpcUrl,
            userId: userId ?? $"{NeuronUnderTest.Value}-{Guid.NewGuid():N}");

    // Sugar: open a session and send the first chat turn in one call.
    protected async Task<NeuronSession> Chat(string prompt)
    {
        var s = Open();
        await s.Chat(prompt);
        return s;
    }

    public async ValueTask DisposeAsync()
    {
        if (_playwright is not null) await _playwright.DisposeAsync();
        _channel?.Dispose();
        // The fixture is owned by xUnit's IClassFixture lifecycle — do not
        // dispose it here; it survives across the class's [Fact]s.
    }
}
