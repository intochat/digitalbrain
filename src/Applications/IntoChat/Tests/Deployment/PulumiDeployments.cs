namespace IntoChat.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PulumiDeployments
{
    public const string Name = "pulumi-deployments";
}
