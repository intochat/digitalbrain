using Xunit;

namespace Ino.Testing.E2E;

/// <summary>
/// Generic xUnit collection definition wrapper for browser-driven neuron
/// tests. Per-domain test projects subclass with a concrete TAppHost:
/// <code>
/// [CollectionDefinition(nameof(MyBrowserCollection))]
/// public sealed class MyBrowserCollection : InoBrowserCollection&lt;Projects.Ino_AppHost&gt; { }
/// </code>
/// </summary>
public abstract class InoBrowserCollection<TAppHost> : ICollectionFixture<InoBrowserFixture<TAppHost>>
    where TAppHost : class
{
}
