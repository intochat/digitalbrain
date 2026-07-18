using Ino.Testing;
using Xunit;

namespace Ino.E2E.Tests;

[CollectionDefinition(nameof(InoE2ECollection))]
public sealed class InoE2ECollection : Ino.Testing.InoE2ECollection<Projects.Ino_AppHost> { }
