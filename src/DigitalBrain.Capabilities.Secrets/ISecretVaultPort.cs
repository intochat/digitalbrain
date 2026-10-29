using DigitalBrain.Capabilities;

namespace DigitalBrain.Capabilities.Secrets;

/// <summary>How a <see cref="ISecretVaultPort"/> call ended.</summary>
public enum SecretVaultStatus
{
    /// <summary>The call succeeded.</summary>
    Ok = 0,

    /// <summary>The secret is unset or gone.</summary>
    NotFound = 1,

    /// <summary>The caller may not touch the secret.</summary>
    Denied = 2,
}

/// <summary>
/// The neutral answer of a <see cref="ISecretVaultPort"/>: a status plus the stored reference (Set) or value (Resolve).
/// <see cref="ToString"/> never prints the value.
/// </summary>
public sealed record SecretVaultResult
{
    /// <summary>How the call ended.</summary>
    public SecretVaultStatus Status { get; init; }

    /// <summary>The stored reference, for a successful Set.</summary>
    public SecretRef? Secret { get; init; }

    /// <summary>The value, for a successful Resolve.</summary>
    public string? Value { get; init; }

    /// <summary>The secret is unset or gone.</summary>
    public static SecretVaultResult NotFound { get; } = new() { Status = SecretVaultStatus.NotFound };

    /// <summary>The caller may not touch the secret.</summary>
    public static SecretVaultResult Denied { get; } = new() { Status = SecretVaultStatus.Denied };

    /// <summary>A successful Set.</summary>
    public static SecretVaultResult Stored(SecretRef secret) => new() { Status = SecretVaultStatus.Ok, Secret = secret };

    /// <summary>A successful Resolve.</summary>
    public static SecretVaultResult Resolved(string value) => new() { Status = SecretVaultStatus.Ok, Value = value };

    /// <summary><c>vault &lt;status&gt;</c> and the reference; never the value.</summary>
    public override string ToString() => Secret is null ? $"vault {Status}" : $"vault {Status} {Secret}";
}

/// <summary>
/// The SPI a host implements so the runtime's secrets@1 capability reaches its vault (R4-02, D-15). Every member takes the
/// caller the runtime read from the stamped call context; the port authorizes that caller and never sees an unstamped call.
/// Later capability versions add members additively, with fail-closed defaults (09-03: Seal, Open, Named). Resolve also
/// answers named references (<see cref="SecretRef.NamedPrefix"/>) once the runtime has checked the manifest declaration.
/// </summary>
public interface ISecretVaultPort
{
    /// <summary>Stores <paramref name="value"/> as <paramref name="name"/> in the caller's brain.</summary>
    Task<SecretVaultResult> Set(CapabilityCaller caller, string name, string label, string value, CancellationToken cancellationToken);

    /// <summary>Removes <paramref name="name"/> from the caller's brain; an unknown name is <see cref="SecretVaultStatus.Ok"/>.</summary>
    Task<SecretVaultStatus> Remove(CapabilityCaller caller, string name, CancellationToken cancellationToken);

    /// <summary>The value behind <paramref name="secret"/>, if the caller may read it.</summary>
    Task<SecretVaultResult> Resolve(CapabilityCaller caller, SecretRef secret, CancellationToken cancellationToken);

    /// <summary>
    /// Seals <paramref name="plaintext"/> bound to <paramref name="context"/> as associated data; null when the vault cannot seal
    /// for the caller. The default refuses (fail closed), so a port written before 09-03 keeps compiling.
    /// </summary>
    Task<SealedSecret?> Seal(CapabilityCaller caller, string context, ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken)
        => Task.FromResult<SealedSecret?>(null);

    /// <summary>The plaintext of <paramref name="sealedSecret"/> for <paramref name="context"/>; null when it does not open. Refuses by default.</summary>
    Task<byte[]?> Open(CapabilityCaller caller, string context, SealedSecret sealedSecret, CancellationToken cancellationToken)
        => Task.FromResult<byte[]?>(null);

    /// <summary>
    /// The reference of the deployment-provided secret <paramref name="name"/> (<see cref="SecretRef.NamedPrefix"/>; unset when no
    /// value is configured). The runtime has already checked the caller's manifest declares it. Refuses by default.
    /// </summary>
    Task<SecretVaultResult> Named(CapabilityCaller caller, string name, CancellationToken cancellationToken)
        => Task.FromResult(SecretVaultResult.Denied);
}
