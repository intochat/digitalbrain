namespace DigitalBrain.Microsoft.GitHub;

[GenerateSerializer, Alias("db.github.resolve-repository")]
public sealed record ResolveRepository(
    [property: Id(0)] string RepositoryUrl);
