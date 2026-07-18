using Xunit;

namespace Ino.Domains.Genesis.Tests;

/// <summary>
/// Genesis-specific xunit collection backed by <see cref="GenesisTestSiloFixture"/>
/// (instead of the shared <see cref="Ino.Testing.InoTestSiloFixture"/>). The
/// L1 acceptance scenario needs the kernel-pinned <c>Discovery</c> grain to
/// activate on the test silo, which requires <c>ino.silo=kernel</c>
/// metadata + <c>AddPinToSiloPlacement</c> — neither of which the shared
/// fixture wires today.
/// </summary>
[CollectionDefinition(nameof(InoTestCollection))]
public sealed class InoTestCollection : ICollectionFixture<GenesisTestSiloFixture>
{
}
