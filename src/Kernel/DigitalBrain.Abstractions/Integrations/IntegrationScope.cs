namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.integration-scope")]
public enum IntegrationScope
{
    User = 0,
    Workspace = 1,
}

