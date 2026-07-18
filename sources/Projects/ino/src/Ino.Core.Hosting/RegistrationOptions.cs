using Ino.Core;

namespace Ino.Core.Hosting;

public sealed class RegistrationOptions
{
    public DomainId Silo { get; set; }
    public IReadOnlyList<IDomain> Domains { get; set; } = [];

    public IReadOnlyList<Type> BuiltInGrainTypes { get; set; } = [];

    public DomainId BuiltInDomainId { get; set; } = DomainId.From("Ino.Kernel.BuiltIns");
}
