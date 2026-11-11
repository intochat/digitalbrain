namespace DigitalBrain.Units;

/// <summary>One compiler or analyzer diagnostic.</summary>
/// <param name="File">The source file path, or empty when the diagnostic has no location.</param>
/// <param name="Id">The diagnostic id, e.g. <c>CS1525</c> or <c>DB0041</c>.</param>
/// <param name="Line">The 1-based line, or 0 when the diagnostic has no location.</param>
/// <param name="Message">The diagnostic message.</param>
public sealed record SynapseDiagnostic(string File, string Id, int Line, string Message);
