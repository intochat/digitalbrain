namespace DigitalBrain.Units;

/// <summary>
/// Thrown when a unit build attempts to reference disallowed APIs or violates the sandboxed authoring boundary.
/// </summary>
public sealed class UnitRefusedException : Exception
{
    /// <summary>The build that was refused.</summary>
    public BuildRef Build { get; }

    /// <summary>The disallowed reference or reason.</summary>
    public string Disallowed { get; }

    /// <summary>Creates a new refusal exception.</summary>
    public UnitRefusedException(BuildRef build, string disallowed)
        : base($"Build '{build.SourceKey}' ({build.Hash}) was refused: '{disallowed}' is not allowed.")
    {
        Build = build;
        Disallowed = disallowed;
    }
}
