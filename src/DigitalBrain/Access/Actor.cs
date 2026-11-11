using DigitalBrain.Modules;

namespace DigitalBrain.Access;

/// <summary>
/// Who is behind a call. Closed hierarchy: unknown subtypes are rejected by <see cref="AccessPolicy"/>.
/// </summary>
public abstract record Actor
{
    private Actor() { }

    /// <summary>A human user authenticated with principal and account IDs.</summary>
    public sealed record Person(string Principal, string Account) : Actor;

    /// <summary>An agent acting on behalf of a human user.</summary>
    public sealed record Agent(Person For) : Actor;

    /// <summary>An application client acting on behalf of a human user.</summary>
    public sealed record App(string Key, Person For) : Actor;

    /// <summary>An internal module of the host (minted only by <see cref="EdgeAuthority.Module"/>).</summary>
    public sealed record Module(string Id) : Actor;

    /// <summary>The scheduling infrastructure.</summary>
    public sealed record Scheduler : Actor;

    /// <summary>The kernel itself (replaces Platform caller, system grain stamps).</summary>
    public sealed record Kernel : Actor;
}
