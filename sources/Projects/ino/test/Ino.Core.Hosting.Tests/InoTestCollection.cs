using Xunit;

namespace Ino.Core.Hosting.Tests;

/// <summary>
/// Per-assembly collection definition over <see cref="Ino.Testing.InoTestSiloFixture"/>.
/// xunit.v3 requires <c>[CollectionDefinition]</c> to live in the same assembly as the
/// tests that reference it, so we declare this thin wrapper locally and inherit from
/// the reusable <see cref="Ino.Testing.InoTestCollection"/> base.
/// </summary>
[CollectionDefinition(nameof(InoTestCollection))]
public sealed class InoTestCollection : Ino.Testing.InoTestCollection
{
}
