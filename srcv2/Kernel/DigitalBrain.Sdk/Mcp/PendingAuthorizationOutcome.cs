namespace DigitalBrain.Modules.Sdk.Mcp;

[GenerateSerializer]
[Alias("db.mcp.pending-authorization-outcome")]
internal enum PendingAuthorizationOutcome
{
    Open = 0,
    Completed = 1,
    Denied = 2,
}
