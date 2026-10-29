namespace DigitalBrain;

// A class, so it lives outside DigitalBrain.Spi: DigitalBrain.Spi stays interfaces only.

/// <summary>How to start an in-process host (<see cref="Spi.IInProcessHostFactory"/>).</summary>
public sealed class InProcessHostOptions
{
    /// <summary>The home folder; null starts in a fresh temporary folder.</summary>
    public string? Home { get; set; }

    /// <summary>The module classes to load.</summary>
    public IReadOnlyList<Type> Modules { get; set; } = [];

    /// <summary>Configuration values for the host.</summary>
    public IReadOnlyDictionary<string, string?> Configuration { get; set; } = new Dictionary<string, string?>();
}
