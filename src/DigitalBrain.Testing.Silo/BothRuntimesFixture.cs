using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using DigitalBrain;
using DigitalBrain.Invocation;
using DigitalBrain.Runtime.Orleans;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Testing;

// The same fact on both runtimes (R4-05): a class fixture that starts the module under test on TestOS.Local() and on an
// in-process local silo (the factory behind SiloHost.Local, given the runtime BrainLog as its cluster store so stateful and
// publishing neurons work). Facts take a NeuronRuntimeKind theory parameter and ask Runtime(kind) for the shared brain, or
// Start(kind) for one of their own (restarts, own services). No os test kit is involved.
public abstract class BothRuntimesFixture : IAsyncLifetime
{
    private readonly ConcurrentDictionary<NeuronRuntimeKind, Lazy<Task<INeuronRuntime>>> _shared = new();

    // The DigitalBrain module classes ([Module] with a static ModuleManifest) whose plain neurons are under test.
    protected abstract IReadOnlyList<Type> Modules { get; }

    // The brain every runtime of this fixture hosts.
    public virtual BrainId Brain { get; } = new("facts");

    // Silo-only services, applied after the topology and runtime registrations (reminder periods, clocks).
    protected virtual void ConfigureSilo(IServiceCollection services) { }

    // TestOS-only services (06-05): what neuron constructors and Context.Services resolve on TestOS (TestOSOptions.Services);
    // none by default. A fact that needs a service on both kinds registers it here and in ConfigureSilo.
    protected virtual void ConfigureTestOS(IServiceCollection services) { }

    // Silo configuration values.
    protected virtual IReadOnlyDictionary<string, string?> SiloConfiguration { get; } = new Dictionary<string, string?>();

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    // The fixture's shared runtime of kind; facts that share it use their own keys.
    public Task<INeuronRuntime> Runtime(NeuronRuntimeKind kind)
        => _shared.GetOrAdd(kind, value => new Lazy<Task<INeuronRuntime>>(() => Start(value, CancellationToken.None))).Value;

    // A runtime of kind that the caller disposes; home keeps the silo's state folder (null: a fresh temporary one) and
    // siloServices runs after ConfigureSilo (fault injection, controls).
    public async Task<INeuronRuntime> Start(NeuronRuntimeKind kind, CancellationToken ct, string? home = null, Action<IServiceCollection>? siloServices = null)
    {
        var modules = Modules;
        if (modules.Count == 0) { throw new InvalidOperationException("A both-runtimes fixture needs at least one module."); }
        return kind switch
        {
            NeuronRuntimeKind.TestOS => TestOSRuntime.Start(Brain, modules, TestOSServices()),
            NeuronRuntimeKind.Silo => await SiloRuntime.Start(Brain, modules, services => { ConfigureSilo(services); siloServices?.Invoke(services); },
                SiloConfiguration, home, ct).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var runtime in _shared.Values.Where(lazy => lazy.IsValueCreated).Select(lazy => lazy.Value))
        {
            if (runtime.IsCompletedSuccessfully) { await runtime.Result.DisposeAsync().ConfigureAwait(false); }
        }
        GC.SuppressFinalize(this);
    }

    private ServiceProvider? TestOSServices()
    {
        var services = new ServiceCollection();
        ConfigureTestOS(services);
        return services.Count == 0 ? null : services.BuildServiceProvider();
    }

    internal static string AliasOf(Type signal) => signal.GetCustomAttribute<SignalAttribute>(inherit: false)?.Alias ?? signal.FullName!;

    private sealed class TestOSRuntime(TestOS os, BrainId brain) : INeuronRuntime
    {
        public static TestOSRuntime Start(BrainId brain, IReadOnlyList<Type> modules, IServiceProvider? services = null)
        {
            var os = TestOS.Local(options => options.Services = services);
            foreach (var assembly in modules.Select(module => module.Assembly).Distinct()) { os.Register(assembly); }
            return new TestOSRuntime(os, brain);
        }

        public NeuronRuntimeKind Kind => NeuronRuntimeKind.TestOS;

        public BrainId Brain => brain;

        public IServiceProvider Services => os.Services;

        public T Get<T>(NeuronId id) where T : class => os.Get<T>(id);

        public Task Idle() => os.Idle();

        public async Task Elapse(TimeSpan by, CancellationToken ct)
        {
            os.Clock.Advance(by);
            await os.Idle().ConfigureAwait(false);
        }

        public Task<IReadOnlyList<T>> Signals<T>(NeuronId publisher, CancellationToken ct) where T : Signal
            => Task.FromResult<IReadOnlyList<T>>([.. os.Journal(publisher.Brain).Signals(publisher).Select(entry => entry.Signal).OfType<T>()]);

        public Task Restart(CancellationToken ct) => os.Restart();

        public ValueTask DisposeAsync() => os.DisposeAsync();
    }

    private sealed class SiloRuntime(IInProcessHost host, BrainId brain) : INeuronRuntime
    {
        public static async Task<SiloRuntime> Start(BrainId brain, IReadOnlyList<Type> modules, Action<IServiceCollection> configure,
            IReadOnlyDictionary<string, string?> configuration, string? home, CancellationToken ct)
        {
            Assembly[] assemblies = [.. modules.Select(module => module.Assembly).Distinct()];
            var factory = new LocalSiloHostFactory(new BrainStorageRegistration(assemblies), configure);
            var options = new InProcessHostOptions { Home = home, Modules = modules, Configuration = configuration };
            var host = await factory.Start(options, ct).ConfigureAwait(false);
            return new SiloRuntime(host, brain);
        }

        public NeuronRuntimeKind Kind => NeuronRuntimeKind.Silo;

        public BrainId Brain => brain;

        public IServiceProvider Services => host.Services;

        public T Get<T>(NeuronId id) where T : class => SiloNeurons.Get<T>(host.Services, id);

        public Task Idle() => Task.CompletedTask;

        public Task Elapse(TimeSpan by, CancellationToken ct) => Task.Delay(by, ct);

        public async Task<IReadOnlyList<T>> Signals<T>(NeuronId publisher, CancellationToken ct) where T : Signal
        {
            var journal = host.Services.GetRequiredService<IBrainJournalWriter>();
            await journal.Claim(publisher.Brain.Value, ct).ConfigureAwait(false);
            var page = await journal.ReadSince(publisher.Brain.Value, publisher.ToString(), 0, 1_000, ct).ConfigureAwait(false);
            var alias = AliasOf(typeof(T));
            return [.. page.Signals.Where(entry => entry.Type == alias).Select(entry => JsonSerializer.Deserialize<T>(entry.Json.Span, RuntimeProtocol.Json)!)];
        }

        public Task Restart(CancellationToken ct) => host.Restart(ct);

        public ValueTask DisposeAsync() => host.DisposeAsync();
    }
}
