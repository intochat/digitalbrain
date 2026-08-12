using DigitalBrain.Core;
namespace DigitalBrain.Repository;

[GenerateSerializer]
[Alias("db.repository-state")]
internal sealed record RepoState(
    [property: Id(0)] string RootPath,
    [property: Id(1)] DateTimeOffset OpenedAt);
