using Xunit;

namespace Ino.Kernel.Tests;

[CollectionDefinition(nameof(InoTestCollection))]
public sealed class InoTestCollection : Ino.Testing.InoTestCollection
{
}
