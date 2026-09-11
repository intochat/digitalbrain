namespace DigitalBrain.Google;

[GenerateSerializer, Alias("db.gmail.unavailable")]
public sealed class GmailUnavailableException(string message) : InvalidOperationException(message);

[GenerateSerializer, Alias("db.gmail.not-connected")]
public sealed class GmailNotConnectedException(string message = "Gmail is not connected. Reconnect Gmail.") : InvalidOperationException(message);

[GenerateSerializer, Alias("db.gmail.unreachable")]
public sealed class GmailUnreachableException(string message) : InvalidOperationException(message);
