using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Runtime.Orleans.Capabilities;

/// <summary>
/// secrets@1 over the host's <see cref="ISecretVaultPort"/> (R4-02, D-02, D-15). A thin adapter: every member reads the caller
/// stamped on the call (<see cref="RuntimeCallerContext"/>), passes it to the port and maps the port's NotFound/Denied to
/// <see cref="SecretNotFound"/>/<see cref="SecretDenied"/>. A call with no stamped caller is denied
/// (<see cref="UnauthorizedAccessException"/>), never defaulted. Logs carry names and references, never values. Named lookups
/// and named references (<see cref="SecretRef.NamedPrefix"/>) require the caller's module to declare the secret in its
/// manifest (<see cref="Declarations"/>, fail closed) before the port is called.
/// </summary>
public sealed partial class SecretsCapability : ISecrets
{
    private readonly ISecretVaultPort _port;
    private readonly ILogger _logger;

    /// <summary>Creates the capability over <paramref name="port"/>.</summary>
    public SecretsCapability(ISecretVaultPort port, ILogger<SecretsCapability>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(port);
        _port = port;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>The module declarations named secrets are checked against; <see cref="ModuleDeclarations.None"/> by default.</summary>
    public ModuleDeclarations Declarations { get; init; } = ModuleDeclarations.None;

    /// <inheritdoc />
    public async Task<SecretRef> Set(string name, string label, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        var caller = RuntimeCallerContext.Required(Capability.Secrets.V1Id);
        var result = await _port.Set(caller, name, label ?? "", value, cancellationToken).ConfigureAwait(false);
        switch (result.Status)
        {
            case SecretVaultStatus.Ok when result.Secret is not null:
                Stored(_logger, result.Secret.Reference, caller.PrincipalId);
                return result.Secret;
            case SecretVaultStatus.Denied:
                Refused(_logger, name, caller.PrincipalId);
                throw new SecretDenied(name);
            default:
                throw new InvalidOperationException($"secrets@1: the vault port answered {result} to set {name}.");
        }
    }

    /// <inheritdoc />
    public async Task Remove(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var caller = RuntimeCallerContext.Required(Capability.Secrets.V1Id);
        var status = await _port.Remove(caller, name, cancellationToken).ConfigureAwait(false);
        if (status == SecretVaultStatus.Denied)
        {
            Refused(_logger, name, caller.PrincipalId);
            throw new SecretDenied(name);
        }

        Removed(_logger, name, caller.PrincipalId);
    }

    /// <inheritdoc />
    public async Task<string> Resolve(SecretRef secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var caller = RuntimeCallerContext.Required(Capability.Secrets.V1Id);
        var name = NameOf(secret);
        if (secret.IsNamed) { RequireDeclared(caller, name = secret.Reference[SecretRef.NamedPrefix.Length..]); }
        var result = await _port.Resolve(caller, secret, cancellationToken).ConfigureAwait(false);
        switch (result.Status)
        {
            case SecretVaultStatus.Ok when result.Value is not null:
                Resolved(_logger, secret.Reference, caller.PrincipalId);
                return result.Value;
            case SecretVaultStatus.Denied:
                Refused(_logger, name, caller.PrincipalId);
                throw new SecretDenied(name);
            default:
                throw new SecretNotFound(name);
        }
    }

    /// <inheritdoc />
    public async Task<SealedSecret> Seal(string context, ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        var caller = RuntimeCallerContext.Required(Capability.Secrets.V1Id);
        var sealedSecret = await _port.Seal(caller, context, plaintext, cancellationToken).ConfigureAwait(false);
        if (sealedSecret is null)
        {
            Refused(_logger, context, caller.PrincipalId);
            throw new SecretDenied(context);
        }

        return sealedSecret;
    }

    /// <inheritdoc />
    public async Task<byte[]> Open(string context, SealedSecret sealedSecret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        ArgumentNullException.ThrowIfNull(sealedSecret);
        var caller = RuntimeCallerContext.Required(Capability.Secrets.V1Id);
        var plaintext = await _port.Open(caller, context, sealedSecret, cancellationToken).ConfigureAwait(false);
        if (plaintext is null)
        {
            Refused(_logger, context, caller.PrincipalId);
            throw new SecretDenied(context);
        }

        return plaintext;
    }

    /// <inheritdoc />
    public async Task<SecretRef> Named(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var caller = RuntimeCallerContext.Required(Capability.Secrets.V1Id);
        RequireDeclared(caller, name);
        var result = await _port.Named(caller, name, cancellationToken).ConfigureAwait(false);
        switch (result.Status)
        {
            case SecretVaultStatus.Ok when result.Secret is not null:
                return result.Secret;
            case SecretVaultStatus.NotFound:
                return SecretRef.Unset(SecretRef.NamedPrefix + name, name);
            default:
                Refused(_logger, name, caller.PrincipalId);
                throw new SecretDenied(name);
        }
    }

    private void RequireDeclared(CapabilityCaller caller, string name)
    {
        if (Declarations.DeclaresSecret(caller.AppId, name)) { return; }
        Refused(_logger, name, caller.PrincipalId);
        throw new SecretDenied(name);
    }

    private static string NameOf(SecretRef secret)
    {
        var reference = secret.Reference ?? "";
        var slash = reference.LastIndexOf('/');
        var name = slash < 0 ? reference : reference[(slash + 1)..];
        return string.IsNullOrWhiteSpace(name) ? "(empty)" : name;
    }

    [LoggerMessage(EventId = 5121, EventName = "SecretStored", Level = LogLevel.Debug, Message = "secrets@1 stored {Reference} for {Principal}.")]
    private static partial void Stored(ILogger logger, string reference, string principal);

    [LoggerMessage(EventId = 5122, EventName = "SecretRemoved", Level = LogLevel.Debug, Message = "secrets@1 removed {Name} for {Principal}.")]
    private static partial void Removed(ILogger logger, string name, string principal);

    [LoggerMessage(EventId = 5123, EventName = "SecretResolved", Level = LogLevel.Debug, Message = "secrets@1 resolved {Reference} for {Principal}.")]
    private static partial void Resolved(ILogger logger, string reference, string principal);

    [LoggerMessage(EventId = 5124, EventName = "SecretDenied", Level = LogLevel.Information, Message = "secrets@1 denied {Name} to {Principal}.")]
    private static partial void Refused(ILogger logger, string name, string principal);
}
