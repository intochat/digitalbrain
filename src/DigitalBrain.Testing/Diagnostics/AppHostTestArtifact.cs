using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aspire.Hosting.ApplicationModel;
using Xunit;

namespace DigitalBrain.Testing;

public sealed class AppHostTestArtifact
{
    internal const int MaximumCollectorFailures = 32;
    internal const int MaximumCommandTransitions = 256;
    internal const int MaximumLogLineLength = 4096;
    internal const int MaximumLogsPerResource = 200;
    internal const int MaximumNotifications = 512;
    internal const int MaximumUtf8Bytes = 2 * 1024 * 1024;

    private string? _json;

    private AppHostTestArtifact(
        string? requestedResource,
        string operation,
        IReadOnlyList<string> knownResourceIds,
        IReadOnlyList<AppHostResourceEvidence> resources,
        IReadOnlyList<AppHostNotificationEvidence> notifications,
        IReadOnlyList<AppHostCommandEvidence> commands,
        IReadOnlyList<string> collectorFailures,
        string cleanupStage,
        string cleanupResult)
    {
        RequestedResource = requestedResource;
        Operation = operation;
        KnownResourceIds = knownResourceIds;
        Resources = resources;
        Notifications = notifications;
        Commands = commands;
        CollectorFailures = collectorFailures;
        CleanupStage = cleanupStage;
        CleanupResult = cleanupResult;
    }

    public string? RequestedResource { get; }

    public string Operation { get; }

    public IReadOnlyList<string> KnownResourceIds { get; }

    public IReadOnlyList<AppHostResourceEvidence> Resources { get; }

    public IReadOnlyList<AppHostNotificationEvidence> Notifications { get; }

    public IReadOnlyList<AppHostCommandEvidence> Commands { get; }

    public IReadOnlyList<string> CollectorFailures { get; }

    public string CleanupStage { get; }

    public string CleanupResult { get; }

    public string ToJson()
        => _json
            ?? throw new InvalidOperationException(
                "The AppHost test artifact was not finalized.");

    internal static AppHostTestArtifact Create(
        string? requestedResource,
        string operation,
        IReadOnlyList<string> knownResourceIds,
        IReadOnlyList<AppHostResourceEvidence> resources,
        IReadOnlyList<AppHostNotificationEvidence> notifications,
        IReadOnlyList<AppHostCommandEvidence> commands,
        IReadOnlyList<string> collectorFailures,
        string cleanupStage,
        string cleanupResult)
    {
        var retainedResources = resources
            .Select(resource => new ResourceDraft(resource))
            .ToArray();
        var retainedNotifications = notifications.ToList();
        var retainedCommands = commands.ToList();
        var retainedCollectorFailures = collectorFailures.ToList();

        while (true)
        {
            var artifact = new AppHostTestArtifact(
                requestedResource,
                operation,
                Freeze(
                    knownResourceIds
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)),
                Freeze(retainedResources.Select(draft => draft.Freeze())),
                Freeze(retainedNotifications),
                Freeze(retainedCommands),
                Freeze(retainedCollectorFailures),
                cleanupStage,
                cleanupResult);
            var json = JsonSerializer.Serialize(
                artifact,
                AppHostTestJsonContext.Default.AppHostTestArtifact);

            if (Encoding.UTF8.GetByteCount(json) <= MaximumUtf8Bytes)
            {
                artifact._json = json;
                return artifact;
            }

            var resourceWithOldestLog = retainedResources
                .Where(resource => resource.Logs.Count > 0)
                .OrderBy(resource => resource.Logs[0].Sequence)
                .ThenBy(resource => resource.ResourceId, StringComparer.Ordinal)
                .FirstOrDefault();

            if (resourceWithOldestLog is not null)
            {
                resourceWithOldestLog.Logs.RemoveAt(0);
                continue;
            }

            if (retainedNotifications.Count > 0)
            {
                retainedNotifications.RemoveAt(0);
                continue;
            }

            if (retainedCommands.Count > 0)
            {
                retainedCommands.RemoveAt(0);
                continue;
            }

            if (retainedCollectorFailures.Count > 0)
            {
                retainedCollectorFailures.RemoveAt(0);
                continue;
            }

            throw new InvalidOperationException(
                "The bounded AppHost test artifact exceeded its two MiB serialized limit after all trimmable evidence was removed.");
        }
    }

    private static ReadOnlyCollection<T> Freeze<T>(
        IEnumerable<T> values)
        => Array.AsReadOnly(values.ToArray());

    private sealed class ResourceDraft
    {
        private readonly AppHostResourceEvidence _resource;

        internal ResourceDraft(AppHostResourceEvidence resource)
        {
            _resource = resource;
            Logs = resource.Logs.ToList();
        }

        internal List<AppHostLogEvidence> Logs { get; }

        internal string ResourceId => _resource.ResourceId;

        internal AppHostResourceEvidence Freeze()
            => new(
                _resource.ResourceId,
                _resource.ResourceType,
                _resource.State,
                _resource.Health,
                _resource.Timestamp,
                _resource.ExitCode,
                _resource.EmittedRuntimeState,
                _resource.Urls,
                AppHostTestArtifact.Freeze(Logs));
    }
}

public sealed class AppHostResourceEvidence
{
    internal AppHostResourceEvidence(
        string resourceId,
        string resourceType,
        string? state,
        string? health,
        DateTimeOffset? timestamp,
        int? exitCode,
        bool emittedRuntimeState,
        IReadOnlyList<string> urls,
        IReadOnlyList<AppHostLogEvidence> logs)
    {
        ResourceId = resourceId;
        ResourceType = resourceType;
        State = state;
        Health = health;
        Timestamp = timestamp;
        ExitCode = exitCode;
        EmittedRuntimeState = emittedRuntimeState;
        Urls = urls;
        Logs = logs;
    }

    public string ResourceId { get; }

    public string ResourceType { get; }

    public string? State { get; }

    public string? Health { get; }

    public DateTimeOffset? Timestamp { get; }

    public int? ExitCode { get; }

    public bool EmittedRuntimeState { get; }

    public IReadOnlyList<string> Urls { get; }

    public IReadOnlyList<AppHostLogEvidence> Logs { get; }
}

public sealed class AppHostLogEvidence
{
    internal AppHostLogEvidence(
        long sequence,
        string content,
        bool isError)
    {
        Sequence = sequence;
        Content = content;
        IsError = isError;
    }

    public long Sequence { get; }

    public string Content { get; }

    public bool IsError { get; }
}

public sealed class AppHostNotificationEvidence
{
    internal AppHostNotificationEvidence(
        long sequence,
        string resourceId,
        string? state,
        string? health,
        DateTimeOffset timestamp)
    {
        Sequence = sequence;
        ResourceId = resourceId;
        State = state;
        Health = health;
        Timestamp = timestamp;
    }

    public long Sequence { get; }

    public string ResourceId { get; }

    public string? State { get; }

    public string? Health { get; }

    public DateTimeOffset Timestamp { get; }
}

public sealed class AppHostCommandEvidence
{
    internal AppHostCommandEvidence(
        long sequence,
        string resourceId,
        string command,
        string transition,
        string? detail,
        DateTimeOffset timestamp)
    {
        Sequence = sequence;
        ResourceId = resourceId;
        Command = command;
        Transition = transition;
        Detail = detail;
        Timestamp = timestamp;
    }

    public long Sequence { get; }

    public string ResourceId { get; }

    public string Command { get; }

    public string Transition { get; }

    public string? Detail { get; }

    public DateTimeOffset Timestamp { get; }
}

internal sealed class AppHostTestDiagnostics
{
    private const int MaximumStoredStringLength = 4096;
    private const int MaximumUrlsPerResource = 32;
    private const string Redacted = "[REDACTED]";
    private static readonly string[] SensitiveKeys =
    [
        "authorization",
        "password",
        "api-key",
        "api_key",
        "apikey",
        "secret",
        "token",
    ];
    private static readonly HashSet<string> TerminalStates =
        new(KnownResourceStates.TerminalStates, StringComparer.Ordinal);

    private readonly BoundedRing<string> _collectorFailures =
        new(AppHostTestArtifact.MaximumCollectorFailures);
    private readonly BoundedRing<AppHostCommandEvidence> _commands =
        new(AppHostTestArtifact.MaximumCommandTransitions);
    private readonly Lock _gate = new();
    private readonly BoundedRing<AppHostNotificationEvidence> _notifications =
        new(AppHostTestArtifact.MaximumNotifications);
    private readonly Dictionary<string, ResourceState> _resources =
        new(StringComparer.Ordinal);
    private string _cleanupResult = "not-run";
    private string _cleanupStage = "not-started";
    private Exception? _notificationFailure;
    private long _sequence;

    internal AppHostTestDiagnostics(
        IEnumerable<(string ResourceId, string ResourceType)> resources)
    {
        foreach (var (resourceId, resourceType) in resources
                     .OrderBy(resource => resource.ResourceId, StringComparer.Ordinal))
        {
            var boundedId = Sanitize(resourceId);
            if (!_resources.ContainsKey(boundedId))
            {
                _resources.Add(
                    boundedId,
                    new ResourceState(
                        boundedId,
                        Sanitize(resourceType)));
            }
        }

    }

    internal void RecordNotification(
        string resourceId,
        string resourceType,
        string? state,
        string? health,
        DateTimeOffset timestamp,
        int? exitCode,
        IEnumerable<string> urls)
    {
        lock (_gate)
        {
            var resource = GetOrAddResourceLocked(
                resourceId,
                resourceType);
            resource.ResourceType = Sanitize(resourceType);
            resource.State = state is null ? null : Sanitize(state);
            resource.Health = health is null ? null : Sanitize(health);
            resource.Timestamp = timestamp;
            resource.ExitCode = exitCode;
            resource.EmittedRuntimeState |= resource.State is not null
                && !string.Equals(
                    resource.State,
                    KnownResourceStates.NotStarted,
                    StringComparison.Ordinal)
                && !string.Equals(
                    resource.ResourceType,
                    "Parameter",
                    StringComparison.Ordinal)
                && !string.Equals(
                    resource.ResourceType,
                    nameof(ParameterResource),
                    StringComparison.Ordinal);
            resource.Urls = Array.AsReadOnly(
                urls
                    .Select(SanitizeUrl)
                    .Distinct(StringComparer.Ordinal)
                    .Take(MaximumUrlsPerResource)
                    .ToArray());

            _notifications.Add(new AppHostNotificationEvidence(
                ++_sequence,
                resource.ResourceId,
                resource.State,
                resource.Health,
                timestamp));

            if (resource.State is not null
                && TerminalStates.Contains(resource.State))
            {
                resource.Terminal?.TrySetResult();
            }
        }
    }

    internal void RecordLog(
        string resourceId,
        string content,
        bool isError)
    {
        lock (_gate)
        {
            var resource = GetOrAddResourceLocked(
                resourceId,
                "unknown");
            resource.Logs.Add(new AppHostLogEvidence(
                ++_sequence,
                Sanitize(content),
                isError));
        }
    }

    internal void RecordCommand(
        string resourceId,
        string command,
        string transition,
        string? detail)
    {
        lock (_gate)
        {
            _commands.Add(new AppHostCommandEvidence(
                ++_sequence,
                Sanitize(resourceId),
                Sanitize(command),
                Sanitize(transition),
                detail is null ? null : Sanitize(detail),
                DateTimeOffset.UtcNow));
        }
    }

    internal void RecordCollectorFailure(
        string collector,
        Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        lock (_gate)
        {
            _collectorFailures.Add(
                $"{Sanitize(collector)}: "
                + $"{Sanitize(failure.GetType().FullName ?? failure.GetType().Name)}: "
                + Sanitize(failure.Message));
        }
    }

    internal void FailTerminalWaiters(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        lock (_gate)
        {
            _notificationFailure = failure;
            foreach (var resource in _resources.Values)
            {
                resource.Terminal?.TrySetException(failure);
            }
        }
    }

    internal IReadOnlyList<string> RuntimeResourceIds()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(
                _resources.Values
                    .Where(resource => resource.EmittedRuntimeState)
                    .Select(resource => resource.ResourceId)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
        }
    }

    internal Task WaitForTerminalAsync(
        string resourceId,
        CancellationToken cancellationToken)
    {
        Task terminal;

        lock (_gate)
        {
            if (_notificationFailure is not null)
            {
                terminal = Task.FromException(_notificationFailure);
            }
            else
            {
                var resource = GetOrAddResourceLocked(
                    resourceId,
                    "unknown");
                if (resource.State is not null
                    && TerminalStates.Contains(resource.State))
                {
                    return Task.CompletedTask;
                }

                resource.Terminal ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                terminal = resource.Terminal.Task;
            }
        }

        return terminal.WaitAsync(cancellationToken);
    }

    internal void RecordCleanup(
        string stage,
        string result,
        Exception? failure = null)
    {
        lock (_gate)
        {
            _cleanupStage = Sanitize(stage);
            _cleanupResult = Sanitize(result);
            if (failure is not null)
            {
                _collectorFailures.Add(
                    $"cleanup.{_cleanupStage}: "
                    + $"{Sanitize(failure.GetType().FullName ?? failure.GetType().Name)}: "
                    + Sanitize(failure.Message));
            }
        }
    }

    internal AppHostTestArtifact Snapshot(
        string? requestedResource,
        string operation,
        string? cleanupStage = null,
        string? cleanupResult = null)
    {
        lock (_gate)
        {
            return AppHostTestArtifact.Create(
                requestedResource is null
                    ? null
                    : Sanitize(requestedResource),
                Sanitize(operation),
                _resources.Keys
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                _resources.Values
                    .OrderBy(resource => resource.ResourceId, StringComparer.Ordinal)
                    .Select(resource => resource.Snapshot())
                    .ToArray(),
                _notifications.Snapshot(),
                _commands.Snapshot(),
                _collectorFailures.Snapshot(),
                cleanupStage is null
                    ? _cleanupStage
                    : Sanitize(cleanupStage),
                cleanupResult is null
                    ? _cleanupResult
                    : Sanitize(cleanupResult));
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "xUnit attachment delivery is best effort and must never mask the original AppHost failure.")]
    internal AppHostTestFailureException CaptureFailure(
        string operation,
        string? requestedResource,
        Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        if (failure is AppHostTestFailureException diagnostic)
        {
            return diagnostic;
        }

        var artifact = Snapshot(
            requestedResource,
            operation);
        var resourceText = requestedResource is null
            ? string.Empty
            : $" for resource '{Sanitize(requestedResource)}'";
        var result = new AppHostTestFailureException(
            $"AppHost operation '{Sanitize(operation)}'{resourceText} failed: "
            + Sanitize(failure.Message),
            artifact,
            failure);

        try
        {
            if (TestContext.Current.Attachments?.ContainsKey(
                    AppHostTestFailureException.AttachmentName)
                != true)
            {
                TestContext.Current.AddAttachment(
                    AppHostTestFailureException.AttachmentName,
                    artifact.ToJson());
            }
        }
        catch (Exception)
        {
            // The finalized artifact remains on the exception.
        }

        return result;
    }

    private ResourceState GetOrAddResourceLocked(
        string resourceId,
        string resourceType)
    {
        var boundedId = Sanitize(resourceId);
        if (!_resources.TryGetValue(boundedId, out var resource))
        {
            resource = new ResourceState(
                boundedId,
                Sanitize(resourceType));
            _resources.Add(boundedId, resource);
        }

        return resource;
    }

    private static string Sanitize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        foreach (var key in SensitiveKeys)
        {
            var searchFrom = 0;
            while (searchFrom < value.Length)
            {
                var keyIndex = value.IndexOf(
                    key,
                    searchFrom,
                    StringComparison.OrdinalIgnoreCase);
                if (keyIndex < 0)
                {
                    break;
                }

                var valueIndex = keyIndex + key.Length;
                while (valueIndex < value.Length
                       && char.IsWhiteSpace(value[valueIndex]))
                {
                    valueIndex++;
                }

                if (valueIndex < value.Length
                    && (value[valueIndex] == ':'
                        || value[valueIndex] == '='))
                {
                    valueIndex++;
                    while (valueIndex < value.Length
                           && char.IsWhiteSpace(value[valueIndex]))
                    {
                        valueIndex++;
                    }

                    return Bound(value[..valueIndex] + Redacted);
                }

                searchFrom = keyIndex + key.Length;
            }
        }

        return Bound(value);
    }

    private static string SanitizeUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return "[invalid-url]";
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };

        return Sanitize(builder.Uri.AbsoluteUri);
    }

    private static string Bound(string value)
        => value.Length <= MaximumStoredStringLength
            ? value
            : value[..MaximumStoredStringLength];

    private sealed class ResourceState
    {
        internal ResourceState(
            string resourceId,
            string resourceType)
        {
            ResourceId = resourceId;
            ResourceType = resourceType;
        }

        internal bool EmittedRuntimeState { get; set; }

        internal int? ExitCode { get; set; }

        internal string? Health { get; set; }

        internal BoundedRing<AppHostLogEvidence> Logs { get; } =
            new(AppHostTestArtifact.MaximumLogsPerResource);

        internal string ResourceId { get; }

        internal string ResourceType { get; set; }

        internal string? State { get; set; }

        internal TaskCompletionSource? Terminal { get; set; }

        internal DateTimeOffset? Timestamp { get; set; }

        internal IReadOnlyList<string> Urls { get; set; } =
            Array.Empty<string>();

        internal AppHostResourceEvidence Snapshot()
            => new(
                ResourceId,
                ResourceType,
                State,
                Health,
                Timestamp,
                ExitCode,
                EmittedRuntimeState,
                Array.AsReadOnly(Urls.ToArray()),
                Logs.Snapshot());
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppHostTestArtifact))]
internal sealed partial class AppHostTestJsonContext : JsonSerializerContext;
