using System.Reflection;
using System.Runtime.CompilerServices;

namespace DigitalBrain;

/// <summary>
/// An app: neurons and views a person installs into a brain. Marked <c>[App(id)]</c>.
/// The base provides what every app needs (D-03): typed key parts (<see cref="Key(BrainId)"/>), the request ledger
/// (<see cref="Requests"/>), the presentation wrapper (<see cref="Present{TView}"/>) and the recorded error state
/// (<see cref="RecordError(Exception, string)"/>, <see cref="Swallow(Exception, string, string)"/>).
/// </summary>
public abstract class App
{
    /// <summary>How many swallowed errors <see cref="SwallowedErrors"/> keeps, newest last.</summary>
    public const int SwallowedKept = 64;

    private readonly Lock _gate = new();
    private readonly List<AppError> _swallowed = [];
    private Func<Type, string, object>? _neuronResolver;
    private Func<Type, object>? _capabilityResolver;
    private string? _id;
    private AppError? _error;

    /// <summary>Creates the app.</summary>
    protected App()
    {
    }

    /// <summary>Attaches the host's neuron proxy resolver to this app instance.</summary>
    public void AttachNeuronResolver(Func<Type, string, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _neuronResolver = resolver;
    }

    /// <summary>Attaches a capability resolver to this app instance.</summary>
    public void AttachCapabilityResolver(Func<Type, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _capabilityResolver = resolver;
    }

    /// <summary>Resolves a scoped capability required by this app (e.g. IStorage).</summary>
    protected T GetCapability<T>() where T : class
    {
        if (_capabilityResolver is not null)
        {
            return (T)_capabilityResolver(typeof(T));
        }
        var alias = typeof(T).GetCustomAttribute<DigitalBrain.Contracts.NeuronAliasAttribute>(inherit: false)?.Alias
            ?? typeof(T).Name.ToLowerInvariant();
        return Get<T>(alias);
    }

    /// <summary>A typed interface to a neuron of the current brain by its default neuron type and <paramref name="key"/>.</summary>
    protected T Get<T>(string key) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_neuronResolver is null)
        {
            throw new InvalidOperationException($"App '{AppId}' has no attached neuron resolver.");
        }
        return (T)_neuronResolver(typeof(T), key);
    }

    /// <summary>The frozen app id from <c>[App(id)]</c>.</summary>
    /// <exception cref="InvalidOperationException">The class has no <c>[App(id)]</c> attribute.</exception>
    public string AppId => _id ??= GetType().GetCustomAttribute<AppAttribute>(inherit: false)?.Id
        ?? throw new InvalidOperationException($"{GetType().FullName} has no [App(id)] attribute.");

    /// <summary>The error state the app recorded last, or null when it answers normally. The UI shows it instead of an empty value.</summary>
    public AppError? RecordedError
    {
        get
        {
            lock (_gate) { return _error; }
        }
    }

    /// <summary>The errors the app chose to lose through <see cref="Swallow(Exception, string, string)"/>, with their reasons; the last <see cref="SwallowedKept"/>.</summary>
    public IReadOnlyList<AppError> SwallowedErrors
    {
        get
        {
            lock (_gate) { return [.. _swallowed]; }
        }
    }

    /// <summary>The request ids this app has handled, so a request delivered twice runs once.</summary>
    protected RequestLedger Requests { get; } = new();

    /// <summary>The app's own key in <paramref name="brain"/>: the root every key part of the app hangs from (<c>Key(brain) / "view"</c>).</summary>
    protected AppKey Key(BrainId brain) => new(brain, AppId);

    /// <summary>
    /// Builds the side and main views and hands them to <paramref name="publish"/> (for example <c>IPresenter.Present</c>).
    /// A builder that throws records the error state (<see cref="RecordedError"/>) and nothing is published.
    /// </summary>
    /// <returns>True when the views were published.</returns>
    protected bool Present<TView>(Action<TView?, TView?> publish, Func<TView?> side, Func<TView?> main, [CallerMemberName] string operation = "")
        where TView : class
    {
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(side);
        ArgumentNullException.ThrowIfNull(main);
        TView? sideView;
        TView? mainView;
        try
        {
            sideView = side();
            mainView = main();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            RecordError(error, operation);
            return false;
        }
        publish(sideView, mainView);
        return true;
    }

    /// <summary>Records <paramref name="error"/> as the app's error state, in place of a value the app could not produce.</summary>
    protected void RecordError(Exception error, [CallerMemberName] string operation = "")
    {
        ArgumentNullException.ThrowIfNull(error);
        RecordError(AppError.From(error, operation));
    }

    /// <summary>Records <paramref name="error"/> as the app's error state.</summary>
    protected void RecordError(AppError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (_gate) { _error = error; }
    }

    /// <summary>Clears the recorded error state after the app answered normally again.</summary>
    protected void ClearError()
    {
        lock (_gate) { _error = null; }
    }

    /// <summary>
    /// Loses <paramref name="error"/> on purpose and records why (<see cref="SwallowedErrors"/>). Only for the few cases where losing the
    /// error is the intended behaviour; anything else records the error state or rethrows.
    /// </summary>
    protected void Swallow(Exception error, string reason, [CallerMemberName] string operation = "")
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        lock (_gate)
        {
            _swallowed.Add(AppError.From(error, operation, reason));
            if (_swallowed.Count > SwallowedKept) { _swallowed.RemoveRange(0, _swallowed.Count - SwallowedKept); }
        }
    }
}
