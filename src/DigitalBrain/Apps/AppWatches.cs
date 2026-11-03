namespace DigitalBrain;

/// <summary>Where an app runs: its brain and its own app key (<c>{brain}/apps/{name}</c>), from which watch sources are keyed.</summary>
public readonly record struct AppAddress(string Brain, string App);

/// <summary>Published when an app switches its active root view.</summary>
/// <param name="App">The app key or id.</param>
/// <param name="FromView">The previous root view identifier, or null on initial render.</param>
/// <param name="ToView">The new root view identifier.</param>
/// <param name="Cause">The operation or reaction that caused the navigation.</param>
[Signal("app.navigated")]
public sealed record AppNavigated(string App, string? FromView, string ToView, string Cause) : Signal;

/// <summary>
/// One signal source an app declares in <see cref="App.Watches"/>. A fixed watch listens to one neuron; a set watch listens to
/// the members its <see cref="Select"/> returns and selects again whenever its registry emits <see cref="ChangeType"/>.
/// </summary>
public sealed class AppWatch
{
    internal AppWatch(string name, Type sourceType, Type signalType, Func<AppAddress, string> key, Func<Signal, Task> handle,
        Type? changeType = null, Type? memberType = null, Func<object, AppAddress, CancellationToken, Task<string[]>>? select = null)
    {
        Name = name;
        SourceType = sourceType;
        SignalType = signalType;
        Key = key;
        Handle = handle;
        ChangeType = changeType;
        MemberType = memberType;
        Select = select;
    }

    /// <summary>The watch's name, unique within its app; it names the handler in logs and in the app's shape.</summary>
    public string Name { get; }

    /// <summary>The contract of the watched neuron, or of the registry for a set.</summary>
    public Type SourceType { get; }

    /// <summary>The signal the handler takes.</summary>
    public Type SignalType { get; }

    /// <summary>The key of the watched neuron, or of the registry for a set.</summary>
    public Func<AppAddress, string> Key { get; }

    /// <summary>Runs one delivered signal.</summary>
    public Func<Signal, Task> Handle { get; }

    /// <summary>The registry's change signal that makes a set select again; null for a fixed watch.</summary>
    public Type? ChangeType { get; }

    /// <summary>The members' contract; null for a fixed watch.</summary>
    public Type? MemberType { get; }

    /// <summary>The member keys of a set, read from its registry; null for a fixed watch.</summary>
    public Func<object, AppAddress, CancellationToken, Task<string[]>>? Select { get; }

    /// <summary>Whether the watch follows a runtime-computed set of members rather than one neuron.</summary>
    public bool IsSet => Select is not null;
}

/// <summary>Collects an app's watches. Declaring does no I/O: neurons are named by key and resolved by the host.</summary>
public sealed class WatchBuilder
{
    private readonly List<AppWatch> _watches = [];

    internal IReadOnlyList<AppWatch> Watches => _watches;

    /// <summary>Listens to <typeparamref name="TSignal"/> from the neuron <paramref name="key"/> names.</summary>
    public void On<TSource, TSignal>(string name, Func<AppAddress, string> key, Func<TSignal, Task> handle)
        where TSource : class where TSignal : Signal
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(handle);
        Add(new AppWatch(name, typeof(TSource), typeof(TSignal), key, signal => handle((TSignal)signal)));
    }

    /// <summary>
    /// Listens to <typeparamref name="TSignal"/> from every member key <paramref name="select"/> returns for the registry
    /// <paramref name="registry"/> names, selecting again whenever the registry emits <typeparamref name="TChange"/>.
    /// </summary>
    public void OnEach<TRegistry, TChange, TMember, TSignal>(
        string name,
        Func<AppAddress, string> registry,
        Func<TRegistry, AppAddress, CancellationToken, Task<string[]>> select,
        Func<TSignal, Task> handle)
        where TRegistry : class where TChange : Signal where TMember : class where TSignal : Signal
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(select);
        ArgumentNullException.ThrowIfNull(handle);
        Add(new AppWatch(name, typeof(TRegistry), typeof(TSignal), registry, signal => handle((TSignal)signal),
            typeof(TChange), typeof(TMember), (source, at, ct) => select((TRegistry)source, at, ct)));
    }

    private void Add(AppWatch watch)
    {
        if (_watches.Any(existing => existing.Name == watch.Name))
        { throw new ArgumentException($"A watch named {watch.Name} is already declared.", nameof(watch)); }
        _watches.Add(watch);
    }
}
