using Ino.Testing;
using Xunit;

namespace Ino.Hosting.Tests;

[CollectionDefinition(nameof(InoMultiSiloCollection))]
public sealed class InoMultiSiloCollection : Ino.Testing.InoMultiSiloCollection { }
