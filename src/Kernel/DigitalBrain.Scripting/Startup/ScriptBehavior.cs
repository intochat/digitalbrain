namespace DigitalBrain.Scripting.Startup;

/// <summary>The durable admitted revision running this script, supplied by the host.</summary>
public sealed record ScriptBehavior(string Name, Guid Revision, string SourceHash);
