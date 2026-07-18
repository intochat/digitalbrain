using Ino.Core;
using Ino.Core.Hosting;

namespace Ino.Identity;

/// <summary>
/// IDomain marker for the identity silo. Built-in to ino — ships out of the
/// box, hosts user-identity-adjacent grains. Peer to <c>Kernel</c>,
/// <c>Travel</c>, <c>Taxi</c>: every silo is uniformly an
/// <see cref="IDomain"/> instance, kernel and third-party alike.
/// </summary>
public sealed class Identity : IDomain
{
    public DomainId Id => DomainId.From("identity");
    public string Version => "0.1.0";
    public IReadOnlyList<Capability> DeclaredCapabilities => [];
}
