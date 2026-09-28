namespace DigitalBrain.Salesforce;

/// <summary>Requires a stored refresh token; a refused grant clears the connection.</summary>
[GenerateSerializer, Alias("salesforce.refresh")]
public sealed record RefreshSalesforceConnection;