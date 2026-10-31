using System.Reflection;

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
    private readonly Lock _gate = new();
    private Task _tail = Task.CompletedTask;
    private readonly AppDefinition _definition;
    private TApp? _app;

    internal InstalledApp(TestOS os, AppDefinition definition)
    {
        _os = os;
        _definition = definition;
        Manifest = definition.Manifest;
    }

    /// <summary>The manifest the app was installed with.</summary>
    public AppManifest Manifest { get; }

    /// <summary>The app instance.</summary>
    public TApp App => _app ?? throw new InvalidOperationException($"App '{Manifest.Id}' is not started.");

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
        if (_definition.FindOperation(operation) is not { } method)
        {
            throw new ArgumentException(
                $"App '{Manifest.Id}' has no operation '{operation}'. Operations: {string.Join(", ", Operations)}.", nameof(operation));
        }
        return _os.Track(Turn(method, argument));
    }

    internal Task Start() => _os.Track(Turn(null, null));

    // Turns run one at a time, in request order.
    private Task Turn(MethodInfo? method, object? argument)
    {
        lock (_gate)
        {
            var turn = RunAfter(_tail, method, argument);
            _tail = turn.ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return turn;
        }
    }

    private async Task RunAfter(Task previous, MethodInfo? method, object? argument)
    {
        await previous.ConfigureAwait(false);
        await Task.Yield();
        if (_app is null)
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
            foreach (var sub in _definition.SignalSubscriptions)
            {
                var register = typeof(InstalledApp<TApp>)
                    .GetMethod(nameof(RegisterSignalSubscription), BindingFlags.Instance | BindingFlags.NonPublic)!
                    .MakeGenericMethod(sub.SignalType);
                await ((Task)register.Invoke(this, [sub])!).ConfigureAwait(false);
            }
        }
        try
        {
            if (method is not null)
            {
                await _definition.Invoke(_app, method.Name, argument).ConfigureAwait(false);
            }
        }
        finally
        {
            Refresh();
        }
    }

    private Task<IAsyncDisposable> RegisterSignalSubscription<TSignal>(AppSignalSubscription sub) where TSignal : Signal
        => _os.ClusterListeners.Listen<TSignal>(
            $"app:{Manifest.Id}:{sub.OperationName}",
            (publisher, signal, _) => string.Equals(publisher.Key, sub.SourceKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(publisher.Type, sub.SourceKey, StringComparison.OrdinalIgnoreCase)
                ? Turn(sub.Method, signal)
                : Task.CompletedTask);
}
