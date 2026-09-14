using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.propose-edit")]
public sealed record ProposeEdit(
    CommandId Id,
    [property: Id(0)] EditRequest Edit,
    long? ExpectedVersion = null) : Command(Id, ExpectedVersion);
