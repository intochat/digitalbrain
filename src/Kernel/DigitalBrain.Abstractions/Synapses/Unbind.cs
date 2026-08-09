using System.ComponentModel;

namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.unbind")]
[Description("Remove a synapse route by its binding identity")]
public sealed record Unbind([property: Id(0)] Guid BindingId) : RequestSynapse<Unbound>;

[GenerateSerializer]
[Alias("db.unbound")]
[Description("A synapse route was removed")]
public sealed record Unbound([property: Id(0)] Guid BindingId) : Synapse;
