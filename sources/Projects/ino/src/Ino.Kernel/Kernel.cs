using Ino.Core;
using Ino.Core.Hosting;

namespace Ino.Kernel;

/// <summary>
/// IDomain marker for the kernel silo. Built-in to ino — ships out of the
/// box, hosts the gateway + Cortex routing + Discovery + per-user identity-
/// adjacent grains. Peer to <c>Identity</c>, <c>Travel</c>, <c>Taxi</c>: every
/// silo is uniformly an <see cref="IDomain"/> instance, kernel and third-party
/// alike.
/// </summary>
public sealed class Kernel : IDomain
{
    public DomainId Id => DomainId.From("kernel");
    public string Version => "0.1.0";
    public IReadOnlyList<Capability> DeclaredCapabilities => [];
}
