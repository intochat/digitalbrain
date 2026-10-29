namespace DigitalBrain.Capabilities.Secrets;

/// <summary>
/// secrets@1: the named secrets of a brain. Values go in through <see cref="Set"/> and come out only through
/// <see cref="Resolve"/>; everything else carries a <see cref="SecretRef"/>, which never holds the value.
/// </summary>
/// <remarks>
/// There is no caller parameter (D-15): the implementation reads the caller the runtime stamped on the call, so an app
/// cannot name another brain's caller. A caller that may not touch a secret gets <see cref="SecretDenied"/>.
/// </remarks>
[Capability("secrets", 1)]
public interface ISecrets
{
    /// <summary>Stores <paramref name="value"/> under <paramref name="name"/>, replacing any earlier value.</summary>
    /// <returns>A set reference to the stored secret.</returns>
    Task<SecretRef> Set(string name, string label, string value, CancellationToken cancellationToken = default);

    /// <summary>Removes the secret; removing an unknown name is a no-op.</summary>
    Task Remove(string name, CancellationToken cancellationToken = default);

    /// <summary>Returns the value behind <paramref name="secret"/>.</summary>
    /// <exception cref="SecretNotFound">The reference is unset or the secret was removed.</exception>
    /// <exception cref="SecretDenied">The runtime caller may not read the secret.</exception>
    Task<string> Resolve(SecretRef secret, CancellationToken cancellationToken = default);

    /// <summary>
    /// Seals <paramref name="plaintext"/> (for example a TOTP seed) bound to <paramref name="context"/> as associated data (for
    /// example <c>auth/totp/{principalId}</c>). The result holds no plaintext and is safe to store in neuron state.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="context"/> is empty.</exception>
    /// <exception cref="SecretDenied">The runtime caller may not seal.</exception>
    Task<SealedSecret> Seal(string context, ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken = default);

    /// <summary>Opens a value <see cref="Seal"/> produced for the same <paramref name="context"/>.</summary>
    /// <exception cref="SecretDenied">The context differs, the value was tampered with, or the caller may not open it.</exception>
    Task<byte[]> Open(string context, SealedSecret sealedSecret, CancellationToken cancellationToken = default);

    /// <summary>
    /// The reference of a secret the deployment provides under <paramref name="name"/> (for example
    /// <c>signin-issuer:SigningKey</c>), for <see cref="Resolve"/>. The caller's module must declare the secret in its manifest
    /// <c>Secrets</c> list (the declared <c>signin-issuer</c> covers <c>signin-issuer:SigningKey</c>). The reference is
    /// <see cref="SecretStatus.Unset"/> when the deployment configured no value; its <see cref="SecretRef.Reference"/> starts
    /// with <see cref="SecretRef.NamedPrefix"/>.
    /// </summary>
    /// <exception cref="SecretDenied">The caller is not a module, or its manifest does not declare the secret.</exception>
    Task<SecretRef> Named(string name, CancellationToken cancellationToken = default);
}
