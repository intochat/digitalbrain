namespace DigitalBrain.Salesforce;

// Requires a stored refresh token; a refused grant clears the connection.
[GenerateSerializer, Alias("salesforce.refresh")]
public sealed record RefreshSalesforceConnection;