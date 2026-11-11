using System.Collections.Concurrent;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Secrets;

namespace DigitalBrain.Testing.Conformance.Secrets;

/// <summary>
/// An in-memory <see cref="ISecretVaultPort"/>: per-brain named values, references <c>&lt;brain&gt;/&lt;name&gt;</c>, another
/// brain's reference is <see cref="SecretVaultStatus.Denied"/>. Lets the runtime's secrets@1 capability run its conformance kit
/// without a host vault. <see cref="Log"/> records operations by reference, never values. Seal and Open use AES-GCM under a
/// random per-instance key; named secrets are the values given to <see cref="Provide"/> (the runtime checks declarations).
/// </summary>
public sealed class InMemorySecretVault : ISecretVaultPort
{
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _log = new();
    private readonly ConcurrentQueue<CapabilityCaller> _callers = new();
    private readonly ConcurrentDictionary<string, string> _named = new(StringComparer.Ordinal);
    private readonly KitSeal _seal = new();

    /// <summary>Configures the deployment-provided secret <paramref name="name"/>.</summary>
    public void Provide(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _named[name] = value;
    }

    /// <inheritdoc />
    public Task<SealedSecret?> Seal(CapabilityCaller caller, string context, ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        cancellationToken.ThrowIfCancellationRequested();
        _callers.Enqueue(caller);
        _log.Enqueue($"seal {context} by {caller.PrincipalId}");
        return Task.FromResult<SealedSecret?>(_seal.Seal(context, plaintext.Span));
    }

    /// <inheritdoc />
    public Task<byte[]?> Open(CapabilityCaller caller, string context, SealedSecret sealedSecret, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        cancellationToken.ThrowIfCancellationRequested();
        _callers.Enqueue(caller);
        var plaintext = _seal.Open(context, sealedSecret);
        _log.Enqueue(plaintext is null ? $"denied open {context} for {caller.PrincipalId}" : $"open {context} by {caller.PrincipalId}");
        return Task.FromResult(plaintext);
    }

    /// <inheritdoc />
    public Task<SecretVaultResult> Named(CapabilityCaller caller, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();
        _callers.Enqueue(caller);
        var reference = SecretRef.NamedPrefix + name;
        return Task.FromResult(SecretVaultResult.Stored(_named.ContainsKey(name)
            ? new SecretRef { Reference = reference, Label = name, Status = SecretStatus.Set }
            : SecretRef.Unset(reference, name)));
    }

    /// <summary>The operation log lines (references and callers only).</summary>
    public IReadOnlyList<string> Log => [.. _log];

    /// <summary>Every caller the port was handed, in call order.</summary>
    public IReadOnlyList<CapabilityCaller> Callers => [.. _callers];

    /// <inheritdoc />
    public Task<SecretVaultResult> Set(CapabilityCaller caller, string name, string label, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();
        _callers.Enqueue(caller);
        var reference = $"{caller.BrainId}/{name}";
        _values[reference] = value;
        _log.Enqueue($"set {reference} by {caller.PrincipalId}");
        return Task.FromResult(SecretVaultResult.Stored(new SecretRef { Reference = reference, Label = label ?? "", Status = SecretStatus.Set }));
    }

    /// <inheritdoc />
    public Task<SecretVaultStatus> Remove(CapabilityCaller caller, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();
        _callers.Enqueue(caller);
        var reference = $"{caller.BrainId}/{name}";
        _values.TryRemove(reference, out _);
        _log.Enqueue($"remove {reference} by {caller.PrincipalId}");
        return Task.FromResult(SecretVaultStatus.Ok);
    }

    /// <inheritdoc />
    public Task<SecretVaultResult> Resolve(CapabilityCaller caller, SecretRef secret, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(secret);
        cancellationToken.ThrowIfCancellationRequested();
        _callers.Enqueue(caller);
        if (secret.IsNamed)
        {
            _log.Enqueue($"resolve {secret.Reference} by {caller.PrincipalId}");
            return Task.FromResult(_named.TryGetValue(secret.Reference[SecretRef.NamedPrefix.Length..], out var provided)
                ? SecretVaultResult.Resolved(provided)
                : SecretVaultResult.NotFound);
        }

        var reference = secret.Reference;
        var slash = reference.IndexOf('/', StringComparison.Ordinal);
        if (slash < 0 || slash == reference.Length - 1) { return Task.FromResult(SecretVaultResult.NotFound); }
        if (!string.Equals(reference[..slash], caller.BrainId, StringComparison.Ordinal))
        {
            _log.Enqueue($"denied {reference} for {caller.PrincipalId}");
            return Task.FromResult(SecretVaultResult.Denied);
        }

        if (!_values.TryGetValue(reference, out var value))
        {
            _log.Enqueue($"not-found {reference}");
            return Task.FromResult(SecretVaultResult.NotFound);
        }

        _log.Enqueue($"resolve {reference} by {caller.PrincipalId}");
        return Task.FromResult(SecretVaultResult.Resolved(value));
    }
}
