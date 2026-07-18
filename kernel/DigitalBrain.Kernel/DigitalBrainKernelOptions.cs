using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Kernel;

internal sealed class DigitalBrainKernelOptions
{
    internal string Name { get; private set; } = "";

    internal string? ClusterId { get; private set; }

    internal string? ServiceId { get; private set; }

    internal string? ClusteringProviderType { get; private set; }

    internal string? ClusteringServiceKey { get; private set; }

    internal string? ClusteringConnection { get; private set; }

    internal string? ReminderProviderType { get; private set; }

    internal string? ReminderServiceKey { get; private set; }

    internal string? ReminderConnection { get; private set; }

    internal string? GrainStorageProviderType { get; private set; }

    internal string? GrainStorageServiceKey { get; private set; }

    internal string? GrainStorageConnection { get; private set; }

    internal string? StreamProviderType { get; private set; }

    internal string? StreamServiceKey { get; private set; }

    internal string? StreamConnection { get; private set; }

    internal string JournalServiceKey { get; private set; } = "";

    internal string JournalContainerName { get; private set; } = "";

    internal string? JournalConnection { get; private set; }

    internal string OutboxServiceKey { get; private set; } = "";

    internal string? OutboxConnection { get; private set; }

    internal string? ClusteringStorage { get; private set; }

    internal string? ReminderStorage { get; private set; }

    internal string? GrainStorage { get; private set; }

    internal string? JournalStorage { get; private set; }

    internal string? StreamStorage { get; private set; }

    internal string? OutboxStorage { get; private set; }

    internal void Load(IConfiguration configuration, string name)
    {
        Name = name;
        ClusterId = configuration["Orleans:ClusterId"];
        ServiceId = configuration["Orleans:ServiceId"];
        ClusteringProviderType = configuration["Orleans:Clustering:ProviderType"];
        ClusteringServiceKey = configuration["Orleans:Clustering:ServiceKey"];
        ClusteringConnection = GetConnectionString(
            configuration,
            ClusteringServiceKey);
        ReminderProviderType = configuration["Orleans:Reminders:ProviderType"];
        ReminderServiceKey = configuration["Orleans:Reminders:ServiceKey"];
        ReminderConnection = GetConnectionString(
            configuration,
            ReminderServiceKey);
        GrainStorageProviderType =
            configuration["Orleans:GrainStorage:Default:ProviderType"];
        GrainStorageServiceKey =
            configuration["Orleans:GrainStorage:Default:ServiceKey"];
        GrainStorageConnection = GetConnectionString(
            configuration,
            GrainStorageServiceKey);
        StreamProviderType =
            configuration["Orleans:Streaming:NeuronNotification:ProviderType"];
        StreamServiceKey =
            configuration["Orleans:Streaming:NeuronNotification:ServiceKey"];
        StreamConnection = GetConnectionString(
            configuration,
            StreamServiceKey);
        JournalServiceKey = $"{name}-journal";
        JournalContainerName = $"{name}-journals";
        JournalConnection = configuration.GetConnectionString(
            JournalServiceKey);
        OutboxServiceKey = $"{name}-outbox";
        OutboxConnection = configuration.GetConnectionString(
            OutboxServiceKey);
        ClusteringStorage = configuration["DigitalBrain:Storage:Clustering"];
        ReminderStorage = configuration["DigitalBrain:Storage:Reminders"];
        GrainStorage = configuration["DigitalBrain:Storage:GrainState"];
        JournalStorage = configuration["DigitalBrain:Storage:Journal"];
        StreamStorage = configuration["DigitalBrain:Storage:Streams"];
        OutboxStorage = configuration["DigitalBrain:Storage:Outbox"];
    }

    private static string? GetConnectionString(
        IConfiguration configuration,
        string? serviceKey) =>
        string.IsNullOrWhiteSpace(serviceKey)
            ? null
            : configuration.GetConnectionString(serviceKey);
}

internal sealed class DigitalBrainKernelOptionsValidator
    : IValidateOptions<DigitalBrainKernelOptions>
{
    private const string AzureTableStorage = "AzureTableStorage";
    private const string AzureBlobStorage = "AzureBlobStorage";
    private const string AzureQueueStorage = "AzureQueueStorage";

    public ValidateOptionsResult Validate(
        string? name,
        DigitalBrainKernelOptions options)
    {
        List<string> failures = [];
        RequireValue(options.ClusterId, "Orleans:ClusterId", failures);
        RequireValue(options.ServiceId, "Orleans:ServiceId", failures);
        RequireProvider(
            options.ClusteringProviderType,
            AzureTableStorage,
            "Orleans:Clustering:ProviderType",
            failures);
        RequireValue(
            options.ClusteringServiceKey,
            "Orleans:Clustering:ServiceKey",
            failures);
        RequireStorage(
            options.ClusteringConnection,
            ConnectionKey(options.ClusteringServiceKey),
            failures);
        RequireProvider(
            options.ReminderProviderType,
            AzureTableStorage,
            "Orleans:Reminders:ProviderType",
            failures);
        RequireValue(
            options.ReminderServiceKey,
            "Orleans:Reminders:ServiceKey",
            failures);
        RequireStorage(
            options.ReminderConnection,
            ConnectionKey(options.ReminderServiceKey),
            failures);
        RequireProvider(
            options.GrainStorageProviderType,
            AzureBlobStorage,
            "Orleans:GrainStorage:Default:ProviderType",
            failures);
        RequireValue(
            options.GrainStorageServiceKey,
            "Orleans:GrainStorage:Default:ServiceKey",
            failures);
        RequireStorage(
            options.GrainStorageConnection,
            ConnectionKey(options.GrainStorageServiceKey),
            failures);
        RequireProvider(
            options.StreamProviderType,
            AzureQueueStorage,
            "Orleans:Streaming:NeuronNotification:ProviderType",
            failures);
        RequireValue(
            options.StreamServiceKey,
            "Orleans:Streaming:NeuronNotification:ServiceKey",
            failures);
        RequireStorage(
            options.StreamConnection,
            ConnectionKey(options.StreamServiceKey),
            failures);
        RequireStorage(
            options.JournalConnection,
            ConnectionKey(options.JournalServiceKey),
            failures);
        RequireStorage(
            options.OutboxConnection,
            ConnectionKey(options.OutboxServiceKey),
            failures);
        RequireStorage(
            options.ClusteringStorage,
            "DigitalBrain:Storage:Clustering",
            failures);
        RequireStorage(
            options.ReminderStorage,
            "DigitalBrain:Storage:Reminders",
            failures);
        RequireStorage(
            options.GrainStorage,
            "DigitalBrain:Storage:GrainState",
            failures);
        RequireStorage(
            options.JournalStorage,
            "DigitalBrain:Storage:Journal",
            failures);
        RequireStorage(
            options.StreamStorage,
            "DigitalBrain:Storage:Streams",
            failures);
        RequireStorage(
            options.OutboxStorage,
            "DigitalBrain:Storage:Outbox",
            failures);
        RequireMatchingStorage(
            options.ClusteringConnection,
            options.ClusteringStorage,
            ConnectionKey(options.ClusteringServiceKey),
            "DigitalBrain:Storage:Clustering",
            failures);
        RequireMatchingStorage(
            options.ReminderConnection,
            options.ReminderStorage,
            ConnectionKey(options.ReminderServiceKey),
            "DigitalBrain:Storage:Reminders",
            failures);
        RequireMatchingStorage(
            options.GrainStorageConnection,
            options.GrainStorage,
            ConnectionKey(options.GrainStorageServiceKey),
            "DigitalBrain:Storage:GrainState",
            failures);
        RequireMatchingStorage(
            options.JournalConnection,
            options.JournalStorage,
            ConnectionKey(options.JournalServiceKey),
            "DigitalBrain:Storage:Journal",
            failures);
        RequireMatchingStorage(
            options.StreamConnection,
            options.StreamStorage,
            ConnectionKey(options.StreamServiceKey),
            "DigitalBrain:Storage:Streams",
            failures);
        RequireMatchingStorage(
            options.OutboxConnection,
            options.OutboxStorage,
            ConnectionKey(options.OutboxServiceKey),
            "DigitalBrain:Storage:Outbox",
            failures);

        var serviceKeys = new[]
            {
                options.ClusteringServiceKey,
                options.ReminderServiceKey,
                options.GrainStorageServiceKey,
                options.StreamServiceKey,
                options.JournalServiceKey,
                options.OutboxServiceKey
            }
            .Where(serviceKey => !string.IsNullOrWhiteSpace(serviceKey))
            .ToArray();
        if (serviceKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != serviceKeys.Length)
            failures.Add(
                "DigitalBrain storage service keys must be distinct.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void RequireProvider(
        string? value,
        string expected,
        string key,
        ICollection<string> failures)
    {
        if (!string.Equals(value, expected, StringComparison.Ordinal))
            failures.Add($"{key} must be '{expected}'.");
    }

    private static void RequireValue(
        string? value,
        string key,
        ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
            failures.Add($"{key} is required.");
    }

    private static void RequireStorage(
        string? value,
        string key,
        ICollection<string> failures)
    {
        if (!IsStorageReference(value))
            failures.Add($"{key} must contain a valid Azure Storage connection or service URI.");
    }

    private static void RequireMatchingStorage(
        string? connection,
        string? projectedStorage,
        string connectionKey,
        string projectedStorageKey,
        ICollection<string> failures)
    {
        if (IsStorageReference(connection)
            && IsStorageReference(projectedStorage)
            && !string.Equals(
                connection,
                projectedStorage,
                StringComparison.Ordinal))
        {
            failures.Add(
                $"{connectionKey} must match {projectedStorageKey}.");
        }
    }

    internal static bool IsStorageReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return IsStorageServiceUri(uri);

        Dictionary<string, string> fields =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (var field in value.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = field.Split('=', 2);
            if (parts.Length != 2)
                return false;

            var key = parts[0].Trim();
            var fieldValue = parts[1].Trim();
            if (key.Length == 0 || !fields.TryAdd(key, fieldValue))
                return false;
        }

        if (!fields.TryGetValue("AccountName", out var accountName)
            || string.IsNullOrWhiteSpace(accountName)
            || !fields.TryGetValue("AccountKey", out var accountKey)
            || !HasValidAccountKey(accountKey))
        {
            return false;
        }

        var endpoints = fields
            .Where(field => field.Key.EndsWith(
                "Endpoint",
                StringComparison.OrdinalIgnoreCase))
            .Select(field => field.Value)
            .ToArray();
        if (endpoints.Any(endpoint =>
                !Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
                || !IsStorageServiceUri(endpointUri)))
        {
            return false;
        }

        if (!fields.TryGetValue(
                "DefaultEndpointsProtocol",
                out var defaultProtocol))
        {
            return endpoints.Length > 0
                && IsSdkCompatibleConnectionString(value);
        }

        if (string.Equals(
                defaultProtocol,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            return IsSdkCompatibleConnectionString(value);
        }

        return string.Equals(
                defaultProtocol,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase)
            && endpoints.Length > 0
            && endpoints.All(endpoint =>
                Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
                && endpointUri.IsLoopback)
            && IsSdkCompatibleConnectionString(value);
    }

    private static bool HasValidAccountKey(string accountKey)
    {
        var buffer = new byte[accountKey.Length];
        return Convert.TryFromBase64String(
                accountKey,
                buffer,
                out var bytesWritten)
            && bytesWritten > 0;
    }

    private static bool IsStorageServiceUri(Uri uri) =>
        string.IsNullOrEmpty(uri.UserInfo)
        && (uri.Scheme == Uri.UriSchemeHttps
            || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);

    private static bool IsSdkCompatibleConnectionString(string value)
    {
        try
        {
            _ = new BlobServiceClient(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string ConnectionKey(string? serviceKey) =>
        $"ConnectionStrings:{serviceKey ?? "<missing-service-key>"}";
}

internal sealed record DigitalBrainKernelRegistration(string Name);
