using System.Security.Cryptography;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Identity.Local.Custody;
using DigitalBrain.Modules.Configuration;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Identity.Local.Vault;

/// <summary>Implements <see cref="ISecretVaultPort"/> over <see cref="VaultNeuron"/> and <see cref="LocalSecretSealer"/>.</summary>
public sealed class LocalVaultPort(
    Func<Type, string, object> neuronResolver,
    IMasterKeyCustody custody,
    IConfiguration? configuration = null) : ISecretVaultPort
{
    private const string SecretScheme = "secret://";

    private readonly Func<Type, string, object> _neuronResolver = neuronResolver ?? throw new ArgumentNullException(nameof(neuronResolver));
    private readonly LocalSecretSealer _sealer = new(custody ?? throw new ArgumentNullException(nameof(custody)));
    private readonly IConfiguration? _configuration = configuration;

    /// <inheritdoc />
    public async Task<SecretVaultResult> Set(
        CapabilityCaller caller,
        string name,
        string label,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var stored = await Vault(caller).Set(Owner(caller), name, label ?? "", value).ConfigureAwait(false);
            return SecretVaultResult.Stored(stored);
        }
        catch (InvalidOperationException)
        {
            return SecretVaultResult.Denied;
        }
    }

    /// <inheritdoc />
    public async Task<SecretVaultStatus> Remove(
        CapabilityCaller caller,
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await Vault(caller).Remove(Owner(caller), name).ConfigureAwait(false);
            return SecretVaultStatus.Ok;
        }
        catch (InvalidOperationException)
        {
            return SecretVaultStatus.Denied;
        }
    }

    /// <inheritdoc />
    public async Task<SecretVaultResult> Resolve(
        CapabilityCaller caller,
        SecretRef secret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(secret);
        cancellationToken.ThrowIfCancellationRequested();
        if (secret.IsNamed)
        {
            var deployed = DeploymentValue(caller, secret.Reference[SecretRef.NamedPrefix.Length..]);
            return !string.IsNullOrEmpty(deployed)
                ? SecretVaultResult.Resolved(deployed)
                : SecretVaultResult.NotFound;
        }

        var reference = secret.Reference ?? "";
        if (!reference.StartsWith(SecretScheme, StringComparison.Ordinal))
        {
            return SecretVaultResult.NotFound;
        }

        var remainder = reference[SecretScheme.Length..];
        var slash = remainder.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == remainder.Length - 1)
        {
            return SecretVaultResult.NotFound;
        }

        var owner = remainder[..slash];
        if (!string.Equals(owner, caller.PrincipalId, StringComparison.Ordinal))
        {
            return SecretVaultResult.Denied;
        }

        try
        {
            var resolved = await Vault(caller).Resolve(Platform(caller), secret).ConfigureAwait(false);
            return SecretVaultResult.Resolved(resolved);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("another owner", StringComparison.OrdinalIgnoreCase))
        {
            return SecretVaultResult.Denied;
        }
        catch (InvalidOperationException)
        {
            return SecretVaultResult.NotFound;
        }
    }

    /// <inheritdoc />
    public async Task<SealedSecret?> Seal(
        CapabilityCaller caller,
        string context,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(context) || string.IsNullOrWhiteSpace(caller.BrainId))
        {
            return null;
        }

        var payload = context + "\n" + Convert.ToBase64String(plaintext.Span);
        return await _sealer.Seal(caller.BrainId, context, payload).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<byte[]?> Open(
        CapabilityCaller caller,
        string context,
        SealedSecret sealedSecret,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(sealedSecret);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(context) || string.IsNullOrWhiteSpace(caller.BrainId))
        {
            return null;
        }

        string bound;
        try
        {
            bound = await _sealer.Open(caller.BrainId, sealedSecret).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return null;
        }

        var prefix = context + "\n";
        if (!bound.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(bound[prefix.Length..]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task<SecretVaultResult> Named(
        CapabilityCaller caller,
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();
        var reference = SecretRef.NamedPrefix + name;
        return Task.FromResult(SecretVaultResult.Stored(
            string.IsNullOrEmpty(DeploymentValue(caller, name))
                ? SecretRef.Unset(reference, name)
                : new SecretRef { Reference = reference, Label = name, Status = SecretStatus.Set }));
    }

    private IVault Vault(CapabilityCaller caller)
        => (IVault)_neuronResolver(typeof(IVault), IVault.Key(caller.PrincipalId));

    private static CapabilityCaller Owner(CapabilityCaller caller)
        => CapabilityCallers.Of(caller.PrincipalId, caller.AccountId, caller.BrainId, CallerKind.User);

    private static CapabilityCaller Platform(CapabilityCaller caller)
        => CapabilityCallers.Platform(caller.PrincipalId, caller.AccountId, caller.BrainId, caller.ConversationId);

    private string? DeploymentValue(CapabilityCaller caller, string name)
    {
        if (_configuration is null || string.IsNullOrWhiteSpace(caller.AppId) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return _configuration[ModuleConfiguration.ModuleSection(caller.AppId) + ":Secrets:" + name];
    }
}
