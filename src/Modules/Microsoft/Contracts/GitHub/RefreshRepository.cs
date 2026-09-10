using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Microsoft.GitHub;

/// <summary>Requests current repository evidence.</summary>
[GenerateSerializer, Alias("db.github.refresh-repository")]
public sealed record RefreshRepository(
    CommandId Id,
    [property: Id(0)] int? Number = null) : Command(Id);
