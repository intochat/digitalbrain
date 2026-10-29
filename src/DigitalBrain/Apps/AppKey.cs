using System.Globalization;

namespace DigitalBrain;

/// <summary>
/// A typed key of an app: the app id and the parts below it, in one brain. Built from <c>App.Key(brain)</c> and the <c>/</c> operator
/// (<c>Key(brain) / "slot" / index</c>), never by joining strings. A part is non-empty and holds no '/' or control characters.
/// </summary>
public readonly record struct AppKey
{
    /// <summary>The root key of app <paramref name="app"/> in <paramref name="brain"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="app"/> is not a valid key part.</exception>
    public AppKey(BrainId brain, string app)
    {
        Brain = brain;
        Path = Checked(app, nameof(app));
    }

    private AppKey(BrainId brain, string path, bool trusted)
    {
        _ = trusted;
        Brain = brain;
        Path = path;
    }

    /// <summary>The brain the key belongs to; default for a key parsed without one.</summary>
    public BrainId Brain { get; }

    /// <summary>The key path: the parts joined with '/'.</summary>
    public string Path { get; }

    /// <summary>The parts of the key, the app id first.</summary>
    public IReadOnlyList<string> Parts => Path is null ? [] : Path.Split('/');

    /// <summary>The app id, the first part.</summary>
    public string App => Path is null ? string.Empty : Path.Split('/')[0];

    /// <summary>The key one part below this one.</summary>
    /// <exception cref="ArgumentException"><paramref name="part"/> is empty or holds '/' or a control character.</exception>
    /// <exception cref="InvalidOperationException">This is the default key.</exception>
    public AppKey Child(string part)
        => Path is null
            ? throw new InvalidOperationException("The default AppKey has no parts; start from App.Key(brain).")
            : new AppKey(Brain, Path + "/" + Checked(part, nameof(part)), trusted: true);

    /// <summary>The key one part below this one, the number written with the invariant culture.</summary>
    public AppKey Child(long part) => Child(part.ToString(CultureInfo.InvariantCulture));

    /// <summary>The key one part below <paramref name="key"/>.</summary>
    public static AppKey operator /(AppKey key, string part) => key.Child(part);

    /// <summary>The key one part below <paramref name="key"/>, the number written with the invariant culture.</summary>
    public static AppKey operator /(AppKey key, long part) => key.Child(part);

    /// <summary>The key one part below <paramref name="key"/> (the named form of <c>/</c>).</summary>
    public static AppKey Divide(AppKey key, string part) => key.Child(part);

    /// <summary>A key read back from its path, in no brain; for keys that arrive as text.</summary>
    /// <exception cref="ArgumentException">A part of <paramref name="path"/> is empty or holds a control character.</exception>
    public static AppKey Parse(string path) => Parse(default, path);

    /// <summary>A key read back from its path in <paramref name="brain"/>.</summary>
    /// <exception cref="ArgumentException">A part of <paramref name="path"/> is empty or holds a control character.</exception>
    public static AppKey Parse(BrainId brain, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        foreach (var part in path.Split('/')) { Checked(part, nameof(path)); }
        return new AppKey(brain, path, trusted: true);
    }

    /// <summary>The neuron of type <paramref name="type"/> at this key.</summary>
    /// <exception cref="InvalidOperationException">The key has no brain.</exception>
    public NeuronId Neuron(string type)
        => Brain.Value is null
            ? throw new InvalidOperationException($"The key '{Path}' has no brain; build it from App.Key(brain).")
            : new NeuronId(Brain, type, Path);

    /// <summary>The key path, for APIs that take a key as text.</summary>
    public static implicit operator string(AppKey key) => key.ToString();

    /// <summary>The key path.</summary>
    public override string ToString() => Path ?? string.Empty;

    private static string Checked(string part, string name)
        => string.IsNullOrEmpty(part) || part.Any(c => c == '/' || char.IsControl(c))
            ? throw new ArgumentException($"A key part is non-empty and holds no '/' or control characters: '{part}'.", name)
            : part;
}
