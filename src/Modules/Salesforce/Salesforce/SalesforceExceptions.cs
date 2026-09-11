namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("db.salesforce.unavailable")]
public sealed class SalesforceUnavailableException(string message) : InvalidOperationException(message);

[GenerateSerializer, Alias("db.salesforce.not-connected")]
public sealed class SalesforceNotConnectedException(string message = "Salesforce is not connected. Reconnect Salesforce.") : InvalidOperationException(message);

[GenerateSerializer, Alias("db.salesforce.unreachable")]
public sealed class SalesforceUnreachableException(string message) : InvalidOperationException(message);
