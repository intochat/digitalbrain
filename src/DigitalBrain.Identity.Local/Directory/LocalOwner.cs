using DigitalBrain.Capabilities.Identity;

namespace DigitalBrain.Identity.Local.Directory;

/// <summary>Provisions the single local owner account and personal brain on a local host.</summary>
public static class LocalOwner
{
    /// <summary>The local owner's account and personal brain identifier.</summary>
    public const string AccountId = "local";

    /// <summary>The local owner's principal identifier.</summary>
    public const string PrincipalId = "owner";

    /// <summary>Idempotently provisions the local owner account and personal brain via <paramref name="identity"/>.</summary>
    public static async Task<Member> Ensure(IIdentity identity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var provision = await identity.ProvisionOwner(
            new Member
            {
                PrincipalId = PrincipalId,
                AccountId = AccountId,
                BrainId = AccountId,
                Role = MemberRole.Owner,
                DisplayName = "Owner",
            },
            ct).ConfigureAwait(false);
        return provision.Member;
    }
}
