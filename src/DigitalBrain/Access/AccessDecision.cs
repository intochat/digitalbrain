namespace DigitalBrain.Access;

/// <summary>
/// Specific reason why access was denied to an operation.
/// </summary>
public enum AccessDenial
{
    /// <summary>No denial; access is granted.</summary>
    None = 0,

    /// <summary>The caller's actor subtype is unrecognized by the kernel.</summary>
    UnknownActor = 1,

    /// <summary>The caller acts in a different brain than the target neuron.</summary>
    OutsideBrain = 2,

    /// <summary>The operation is marked [Internal] and can only be invoked from within the cluster or by a module.</summary>
    InternalOnly = 3,

    /// <summary>The invocation carried no authenticated caller context.</summary>
    NoCaller = 4
}

/// <summary>
/// The result of an access policy evaluation.
/// </summary>
public readonly record struct AccessDecision(bool Allowed, AccessDenial Denial, string? Explanation)
{
    /// <summary>Canonical allow decision.</summary>
    public static readonly AccessDecision Allow = new(true, AccessDenial.None, null);

    /// <summary>Creates a denial decision with the given reason and explanation.</summary>
    public static AccessDecision Deny(AccessDenial denial, string explanation) => new(false, denial, explanation);
}
