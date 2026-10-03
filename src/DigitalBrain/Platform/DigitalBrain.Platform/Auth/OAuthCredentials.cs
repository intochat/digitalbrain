using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Auth;
using DigitalBrain.Platform.Contracts.Secrets;
using DigitalBrain.Sdk.Types;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Platform.Auth;

internal sealed class OAuthCredentials(IGrainFactory grains, ILogger<OAuthCredentials> logger) : IOAuthCredentials
{
    public Task<SecretRef> StoreAccessToken(string provider, string owner, string value, CancellationToken ct)
        => Store(provider, owner, "access", value, ct);
    public Task<SecretRef> StoreRefreshToken(string provider, string owner, string value, CancellationToken ct)
        => Store(provider, owner, "refresh", value, ct);
    public Task<string> ResolveAccessToken(string provider, SecretRef reference, CancellationToken ct)
        => Resolve(provider, reference, "access", ct);
    public Task<string> ResolveRefreshToken(string provider, SecretRef reference, CancellationToken ct)
        => Resolve(provider, reference, "refresh", ct);

    private Task<SecretRef> Store(string provider, string owner, string kind, string value, CancellationToken ct)
    {
        var caller = Operation(provider, owner, "store-" + kind);
        return grains.GetGrain<ISecrets>(owner).Set(caller, provider + "." + kind, provider + " " + kind + " token", value, ct);
    }
    private Task<string> Resolve(string provider, SecretRef reference, string kind, CancellationToken ct)
    {
        var owner = reference.OwnerOf();
        var expected = SecretReferences.For(owner, provider + "." + kind, "", true);
        if (reference.Reference != expected.Reference) { throw new UnauthorizedAccessException("The reference is not the requested provider token."); }
        return grains.GetGrain<ISecrets>(owner).Resolve(Operation(provider, owner, "resolve-" + kind), reference, ct);
    }
    private CallerContext Operation(string provider, string owner, string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (string.IsNullOrWhiteSpace(provider) || provider.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
        { throw new ArgumentException("A provider ID is required.", nameof(provider)); }
        var actor = CallerContextStamper.TryGet(out var current) ? current : new CallerContext
        { PrincipalId = provider, AccountId = owner, BrainId = owner, Kind = CallerKind.Platform, StampedBy = TrustedEdge.Platform };
        if (actor.Kind == CallerKind.User && actor.PrincipalId != owner)
        { throw new UnauthorizedAccessException("The token belongs to another owner."); }
        logger.LogInformation("OAuth {Operation} for {Provider}, owner {Owner}, requested by {Actor} in {Account}/{Brain}.",
            operation, provider, owner, actor.PrincipalId, actor.AccountId, actor.BrainId);
        return actor with { Kind = CallerKind.Platform, StampedBy = TrustedEdge.Platform, AppId = provider };
    }
}
