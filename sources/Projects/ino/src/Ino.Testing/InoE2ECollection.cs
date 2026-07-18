using Xunit;

namespace Ino.Testing;

public abstract class InoE2ECollection<TAppHost> : ICollectionFixture<InoTestAppHost<TAppHost>>
    where TAppHost : class
{
}
