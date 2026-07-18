using Xunit;

namespace Ino.Domains.Taxi.Tests;

[CollectionDefinition(nameof(InoTestCollection))]
public sealed class InoTestCollection : Ino.Testing.InoTestCollection
{
}
