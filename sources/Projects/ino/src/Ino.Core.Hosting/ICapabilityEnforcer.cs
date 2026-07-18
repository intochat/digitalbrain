using Ino.Core;

namespace Ino.Core.Hosting;

public interface ICapabilityEnforcer
{
    void AssertCanFire(Caller source, CanonicalTarget target);
    void AssertCanFireBroadcast(Caller source, ReactiveTarget target);
}
