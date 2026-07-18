using Ino.Core;
using Ino.Core.Hosting;

namespace Ino.Testing.Fixture;

public sealed class Gamma : IDomain
{
    public DomainId Id => DomainId.From("Ino.Testing.Fixture.Gamma");
    public string Version => "1.0.0";

    public IReadOnlyList<Capability> DeclaredCapabilities =>
        [ new Capability.Llm(LlmTier.Balanced) ];

    public IReadOnlyDictionary<Type, IReadOnlyList<Capability>> PerGrainCapabilities =>
        new Dictionary<Type, IReadOnlyList<Capability>>
        {
            [typeof(GammaHandler)] = [ new Capability.Llm(LlmTier.Reasoning) ],
        };
}
