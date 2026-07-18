using Xunit;

namespace Ino.Testing;

/// <summary>
/// Reusable <see cref="ICollectionFixture{T}"/> definition over
/// <see cref="InoTestSiloFixture"/>. Consumer test assemblies declare a thin local
/// class with <c>[CollectionDefinition(nameof(InoTestCollection))]</c> that inherits
/// from this type — xunit.v3 enforces that <c>[CollectionDefinition]</c> live in the
/// same assembly as the tests that reference it, so we can't declare one here and
/// reuse it across projects.
///
/// Usage in a downstream test project:
/// <code>
///   [CollectionDefinition(nameof(InoTestCollection))]
///   public sealed class InoTestCollection : Ino.Testing.InoTestCollection { }
///
///   [Collection(nameof(InoTestCollection))]
///   public sealed class MyTests
///   {
///       private readonly InoTestSiloFixture _fixture;
///       public MyTests(InoTestSiloFixture fixture) { _fixture = fixture; }
///   }
/// </code>
/// </summary>
public abstract class InoTestCollection : ICollectionFixture<InoTestSiloFixture>
{
}
