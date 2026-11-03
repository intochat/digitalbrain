using System.Reflection;
using DigitalBrain.Apps.Hosting;

namespace DigitalBrain.Testing;

/// <summary>
/// An app installed into a <see cref="TestOS"/>: requests its operations one at a time, as turns, and reads what it presents.
/// An operation is an <c>[On]</c> method of the app; it is requested by its method name, case-insensitive, with dashes ignored.
/// </summary>
/// <typeparam name="TApp">The <c>[App]</c> class.</typeparam>
public sealed partial class InstalledApp<TApp> : IInstalledApp where TApp : App
{
    App IInstalledApp.Instance => App;

    private readonly TestOS _os;
    private readonly AppDefinition _definition;
    private TApp? _app;
    private AppTurns? _turns;

    internal InstalledApp(TestOS os, AppDefinition definition)
    {
        _os = os;
        _definition = definition;
        Definition = definition;
    }

    /// <summary>The definition the app was installed with.</summary>
    public AppDefinition Definition { get; }

    /// <summary>The app instance.</summary>
    public TApp App => _app ?? throw new InvalidOperationException($"App '{Definition.Id}' is not started.");

    /// <summary>The operations the app offers: the names of its <c>[On]</c> methods, as requested by <see cref="Request(string, object?)"/>.</summary>
    public IReadOnlyList<string> Operations => [.. _definition.Operations.Values.Select(method => method.Name).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Runs the operation <paramref name="operation"/> as a turn and completes when it has. A null <paramref name="argument"/> is the empty input:
    /// an empty string, a default value, or an object read from <c>{}</c>. Any other argument is passed as is when it fits, else converted through JSON.
    /// </summary>
    /// <exception cref="ArgumentException">The app has no such operation.</exception>
    public Task Request(string operation, object? argument = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (_definition.FindOperation(operation) is null)
        {
            throw new ArgumentException(
                $"App '{Definition.Id}' has no operation '{operation}'. Operations: {string.Join(", ", Operations)}.", nameof(operation));
        }
        if (_turns is null)
        {
            throw new InvalidOperationException($"App '{Definition.Id}' is not started.");
        }
        return _os.Track(_turns.Request(operation, argument, CancellationToken.None));
    }

    internal Task Start()
    {
        _app = (TApp)_definition.Create(Service);
        _app.AttachNeuronResolver((contractType, key) =>
        {
            var typeName = contractType.GetCustomAttribute<DigitalBrain.Contracts.DefaultNeuronTypeAttribute>(false)?.Type
                ?? contractType.GetCustomAttribute<DigitalBrain.Contracts.NeuronAliasAttribute>(false)?.Alias
                ?? throw new InvalidOperationException($"Contract {contractType.FullName} has no [DefaultNeuronType] or [NeuronAlias] attribute.");
            var id = new NeuronId(new BrainId("local"), typeName, key);
            try
            {
                return typeof(TestOS).GetMethod(nameof(TestOS.Get))!.MakeGenericMethod(contractType).Invoke(_os, [id])!;
            }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        });
        _app.AttachCapabilityResolver(capabilityType =>
        {
            var alias = capabilityType.GetCustomAttribute<DigitalBrain.Contracts.NeuronAliasAttribute>(false)?.Alias
                ?? capabilityType.GetCustomAttribute<DigitalBrain.Contracts.DefaultNeuronTypeAttribute>(false)?.Type
                ?? capabilityType.Name.ToLowerInvariant();
            var id = new NeuronId(new BrainId("local"), alias, alias);
            try
            {
                return typeof(TestOS).GetMethod(nameof(TestOS.Get))!.MakeGenericMethod(capabilityType).Invoke(_os, [id])!;
            }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        });
        _turns = new AppTurns(_app, _definition, _ =>
        {
            Refresh();
            return Task.CompletedTask;
        });
        return _os.Track(_turns.Enqueue(async _ =>
        {
            foreach (var sub in _definition.SignalSubscriptions)
            {
                var register = typeof(InstalledApp<TApp>)
                    .GetMethod(nameof(RegisterSignalSubscription), BindingFlags.Instance | BindingFlags.NonPublic)!
                    .MakeGenericMethod(sub.SignalType);
                await ((Task)register.Invoke(this, [sub])!).ConfigureAwait(false);
            }
        }));
    }

    private Task<IAsyncDisposable> RegisterSignalSubscription<TSignal>(AppSignalSubscription sub) where TSignal : Signal
        => _os.ClusterListeners.Listen<TSignal>(
            $"app:{Definition.Id}:{sub.OperationName}",
            (publisher, signal, _) => string.Equals(publisher.Key, sub.SourceKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(publisher.Type, sub.SourceKey, StringComparison.OrdinalIgnoreCase)
                ? _os.Track(_turns!.Request(sub.Method.Name, signal, CancellationToken.None))
                : Task.CompletedTask);
}
