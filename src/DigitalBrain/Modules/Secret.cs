namespace DigitalBrain;

/// <summary>A named secret a module reads.</summary>
public sealed record Secret
{
    private Secret(string name) => Name = name;

    /// <summary>The secret name.</summary>
    public string Name { get; }

    /// <summary>A secret by name.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> does not match <c>^[a-z][a-z0-9-]{0,62}$</c>.</exception>
    public static Secret Named(string name) => new(Names.Require(name, nameof(name), "secret"));
}
