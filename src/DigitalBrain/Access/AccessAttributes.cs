namespace DigitalBrain.Access;

/// <summary>
/// Who may call a contract method; the kernel checks it once per call against the stamped caller.
/// Unmarked is the owner rule: the caller's brain is the brain the neuron's key starts with.
/// On an interface it is the contract's default; on a method it wins over the interface.
/// </summary>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, Inherited = false)]
public abstract class AccessAttribute : Attribute;

/// <summary>Any authenticated caller; no brain scope, no grants.</summary>
public sealed class PublicAttribute : AccessAttribute;

/// <summary>One neuron serves every brain: each caller acts as its own brain.</summary>
public sealed class SharedAttribute : AccessAttribute;

/// <summary>The key is an account id: only that account's callers, acting as their selected brain.</summary>
public sealed class PerAccountAttribute : AccessAttribute;

/// <summary>Reachable only from inside the cluster (module code), never from an edge caller.</summary>
public sealed class InternalAttribute : AccessAttribute;
