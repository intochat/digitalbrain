namespace DigitalBrain.Access;

/// <summary>
/// Who is permitted to invoke a neuron operation:
/// <list type="bullet">
/// <item><see cref="Owner"/>: Only callers within the same brain boundary.</item>
/// <item><see cref="Public"/>: Any authenticated caller across brain boundaries.</item>
/// <item><see cref="Shared"/>: One neuron serves every brain: each caller acts as its own brain.</item>
/// <item><see cref="Internal"/>: Reachable only from inside cluster/module code, never from edge.</item>
/// </list>
/// </summary>
public enum OperationAccess
{
    /// <summary>Only callers within the same brain boundary.</summary>
    Owner = 0,

    /// <summary>Any authenticated caller across brain boundaries.</summary>
    Public = 1,

    /// <summary>One neuron serves every brain: each caller acts as its own brain.</summary>
    Shared = 2,

    /// <summary>Reachable only from inside cluster/module code, never from edge.</summary>
    Internal = 3,
}
