using Ino.Core;
using Ino.Core.Hosting;

namespace Ino.Testing.Fixture;

public sealed class Alpha : IDomain
{
    public DomainId Id => DomainId.From("Ino.Testing.Fixture.Alpha");
    public string Version => "1.0.0";
    public IReadOnlyList<Capability> DeclaredCapabilities => [ new Capability.Llm(LlmTier.Balanced) ];
}
