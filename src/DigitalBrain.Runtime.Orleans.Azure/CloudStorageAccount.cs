using Azure.Core;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

namespace DigitalBrain.Runtime.Orleans.Azure;

/// <summary>
/// The one storage account behind <c>IntoChatOS:Cloud:Storage</c> (P2-D-22): an https account endpoint (managed identity
/// through <see cref="DefaultAzureCredential"/>, honouring <c>AZURE_CLIENT_ID</c>) or a connection string (Azurite, CI).
/// Tables, blobs and queues are all derived from it. The value is never formatted into messages.
/// </summary>
public sealed class CloudStorageAccount
{
    private static readonly string[] ServiceLabels = ["blob", "table", "queue"];
    private readonly string? _connectionString;
    private readonly string? _account;
    private readonly string? _suffix;
    private readonly Lazy<TokenCredential>? _credential;

    private CloudStorageAccount(string connectionString) => _connectionString = connectionString;

    private CloudStorageAccount(string account, string suffix)
    {
        _account = account;
        _suffix = suffix;
        _credential = new Lazy<TokenCredential>(Credential);
    }

    /// <summary>True when the account authenticates with the deployment identity (an account URI).</summary>
    public bool UsesIdentity => _connectionString is null;

    /// <summary>Parses the configured value; null with an issue naming <c>IntoChatOS:Cloud:Storage</c> otherwise.</summary>
    public static CloudStorageAccount? TryParse(string? value, out TopologyIssue? issue)
    {
        issue = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            issue = new TopologyIssue(IntoChatOSNames.CloudStorageKey, "is required on Topology=cloud (a storage account URI or connection string).");
            return null;
        }
        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !trimmed.Contains(';', StringComparison.Ordinal))
        {
            var parts = uri.Host.Split('.', 3);
            if (uri.Scheme == Uri.UriSchemeHttps && parts.Length == 3 && parts[0].Length > 0
                && ServiceLabels.Contains(parts[1], StringComparer.OrdinalIgnoreCase))
            {
                return new CloudStorageAccount(parts[0], parts[2]);
            }
            issue = new TopologyIssue(IntoChatOSNames.CloudStorageKey, "must be an https storage account endpoint (https://<account>.blob.<suffix>) or a connection string.");
            return null;
        }
        try
        {
            // Each client parses the connection string; none connects here.
            _ = new BlobServiceClient(trimmed);
            _ = new QueueServiceClient(trimmed);
            _ = new TableServiceClient(trimmed);
            return new CloudStorageAccount(trimmed);
        }
        catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException)
        {
            issue = new TopologyIssue(IntoChatOSNames.CloudStorageKey, "is neither a storage account URI nor a valid connection string.");
            return null;
        }
    }

    /// <summary>Parses the configured value.</summary>
    /// <exception cref="InvalidOperationException">The value is missing or invalid; names the key only.</exception>
    public static CloudStorageAccount Parse(string? value)
        => TryParse(value, out var issue) ?? throw new InvalidOperationException($"{issue!.Key}: {issue.Message}");

    /// <summary>The blob endpoint.</summary>
    public BlobServiceClient Blobs() => _connectionString is { } cs ? new BlobServiceClient(cs) : new BlobServiceClient(Endpoint("blob"), _credential!.Value);

    /// <summary>The table endpoint.</summary>
    public TableServiceClient Tables() => _connectionString is { } cs ? new TableServiceClient(cs) : new TableServiceClient(Endpoint("table"), _credential!.Value);

    /// <summary>The queue endpoint.</summary>
    public QueueServiceClient Queues() => _connectionString is { } cs ? new QueueServiceClient(cs) : new QueueServiceClient(Endpoint("queue"), _credential!.Value);

    /// <inheritdoc />
    public override string ToString() => UsesIdentity ? "CloudStorageAccount(identity)" : "CloudStorageAccount(connection string)";

    internal static TokenCredential Credential()
    {
        var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        return new DefaultAzureCredential(string.IsNullOrWhiteSpace(clientId)
            ? new DefaultAzureCredentialOptions()
            : new DefaultAzureCredentialOptions { ManagedIdentityClientId = clientId.Trim() });
    }

    private Uri Endpoint(string service) => new($"https://{_account}.{service}.{_suffix}/");
}
