using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace DigitalBrain.Testing;

public sealed class BrainTestArtifact
{
    internal const int MaximumEvents = 512;
    internal const int MaximumFaults = 32;
    internal const int MaximumModules = 32;
    internal const int MaximumOwners = 32;
    internal const int MaximumStringLength = 2048;
    internal const int MaximumUtf8Bytes = 1024 * 1024;

    private string? _json;

    private BrainTestArtifact(
        string fixtureId,
        string scopeId,
        IReadOnlyList<string> moduleIds,
        IReadOnlyList<string> owners,
        DateTimeOffset clockOrigin,
        DateTimeOffset clockUtc,
        IReadOnlyList<BrainTestEvent> events,
        IReadOnlyList<BrainTestFault> faults,
        string? cleanupStage)
    {
        FixtureId = fixtureId;
        ScopeId = scopeId;
        ModuleIds = moduleIds;
        Owners = owners;
        ClockOrigin = clockOrigin;
        ClockUtc = clockUtc;
        Events = events;
        Faults = faults;
        CleanupStage = cleanupStage;
    }

    public string FixtureId { get; }

    public string ScopeId { get; }

    public IReadOnlyList<string> ModuleIds { get; }

    public IReadOnlyList<string> Owners { get; }

    public DateTimeOffset ClockOrigin { get; }

    public DateTimeOffset ClockUtc { get; }

    public IReadOnlyList<BrainTestEvent> Events { get; }

    public IReadOnlyList<BrainTestFault> Faults { get; }

    public string? CleanupStage { get; }

    public string ToJson()
        => _json
            ?? throw new InvalidOperationException(
                "The brain test artifact was not finalized.");

    internal static BrainTestArtifact Create(
        string fixtureId,
        string scopeId,
        IReadOnlyList<string> moduleIds,
        IReadOnlyList<string> owners,
        DateTimeOffset clockOrigin,
        DateTimeOffset clockUtc,
        IReadOnlyList<BrainTestEvent> events,
        IReadOnlyList<BrainTestFault> faults,
        string? cleanupStage)
    {
        var retainedEvents = events.ToArray();

        while (true)
        {
            var artifact = new BrainTestArtifact(
                fixtureId,
                scopeId,
                Freeze(moduleIds),
                Freeze(owners),
                clockOrigin,
                clockUtc,
                Freeze(retainedEvents),
                Freeze(faults),
                cleanupStage);
            var json = JsonSerializer.Serialize(
                artifact,
                BrainTestJsonContext.Default.BrainTestArtifact);

            if (Encoding.UTF8.GetByteCount(json) <= MaximumUtf8Bytes)
            {
                artifact._json = json;
                return artifact;
            }

            if (retainedEvents.Length == 0)
            {
                throw new InvalidOperationException(
                    "The bounded brain test artifact exceeded its one MiB serialized limit without any events remaining to trim.");
            }

            retainedEvents = retainedEvents[
                Math.Max(1, retainedEvents.Length / 4)..];
        }
    }

    private static ReadOnlyCollection<T> Freeze<T>(
        IEnumerable<T> values)
        => Array.AsReadOnly(values.ToArray());
}

public sealed class BrainTestEvent
{
    internal BrainTestEvent(
        long sequence,
        string operation,
        string state,
        IReadOnlyDictionary<string, string> metadata)
    {
        Sequence = sequence;
        Operation = operation;
        State = state;
        Metadata = metadata;
    }

    public long Sequence { get; }

    public string Operation { get; }

    public string State { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class BrainTestFault
{
    internal BrainTestFault(
        string target,
        string state)
    {
        Target = target;
        State = state;
    }

    public string Target { get; }

    public string State { get; }
}

internal sealed class BrainTestDiagnostics
{
    private const string Redacted = "[REDACTED]";
    private const int MaximumStoredStringLength =
        BrainTestArtifact.MaximumStringLength / 2;
    private static readonly string[] SensitiveKeys =
    [
        "secret",
        "token",
        "key",
        "authorization",
        "password",
    ];

    private readonly DateTimeOffset _clockOrigin;
    private readonly BoundedRing<BrainTestEvent> _events =
        new(BrainTestArtifact.MaximumEvents);
    private readonly string _fixtureId;
    private readonly Dictionary<JournalFaultHandle, FaultState> _faults = [];
    private readonly Lock _gate = new();
    private readonly IReadOnlyList<string> _moduleIds;
    private readonly List<string> _owners = [];
    private readonly HashSet<string> _ownerSet =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _sensitiveOwnerIds =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _sensitiveOwnerLabels =
        new(StringComparer.Ordinal);
    private readonly string _scopeId;
    private DateTimeOffset _clockUtc;
    private bool _redactAllDerivedIdentifiers;
    private long _sequence;

    internal BrainTestDiagnostics(
        string fixtureId,
        string scopeId,
        IEnumerable<string> moduleIds,
        DateTimeOffset clockOrigin)
    {
        _fixtureId = Bound(fixtureId);
        _scopeId = Bound(scopeId);
        _moduleIds = Array.AsReadOnly(
            moduleIds
                .Select(id => Bound(id))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Take(BrainTestArtifact.MaximumModules)
                .ToArray());
        _clockOrigin = clockOrigin;
        _clockUtc = clockOrigin;
    }

    internal void RecordOwner(
        string label,
        string ownerId)
    {
        lock (_gate)
        {
            var ownerKey = $"owner.{label}";
            if (IsSensitiveKey(ownerKey))
            {
                TrackSensitiveOwnerLocked(label, ownerId);
            }

            var bounded = Sanitize(ownerKey, ownerId);
            if (_owners.Count >= BrainTestArtifact.MaximumOwners
                || !_ownerSet.Add(bounded))
            {
                return;
            }

            _owners.Add(bounded);
        }
    }

    internal void RecordEvent(
        string operation,
        string state,
        params (string Key, string Value)[] metadata)
    {
        lock (_gate)
        {
            RecordEventLocked(operation, state, metadata);
        }
    }

    internal void SetClock(DateTimeOffset value)
    {
        lock (_gate)
        {
            _clockUtc = value;
        }
    }

    internal void TrackFault(
        JournalFaultHandle handle,
        string target)
    {
        lock (_gate)
        {
            if (_faults.Count < BrainTestArtifact.MaximumFaults)
            {
                _faults[handle] = new(
                    Sanitize("target", target),
                    "armed");
            }

            RecordEventLocked(
                "fault.arm",
                "succeeded",
                [("target", target)]);
        }
    }

    internal void RetireFault(
        JournalFaultHandle handle,
        string state)
    {
        lock (_gate)
        {
            _faults.Remove(handle);
            RecordEventLocked(
                "fault.disarm",
                state,
                [("target", handle.Target.ToString())]);
        }
    }

    internal void RecordCleanupLeak(
        JournalFaultHandle handle)
    {
        lock (_gate)
        {
            if (_faults.Count < BrainTestArtifact.MaximumFaults)
            {
                _faults[handle] = new(
                    Sanitize(
                        "target",
                        handle.Target.ToString()),
                    "cleanup-leak");
            }
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Diagnostic attachment delivery is best-effort and must never replace the original framework operation failure preserved by BrainTestFailureException.")]
    internal BrainTestFailureException CaptureFailure(
        string operation,
        Exception failure,
        string? cleanupStage = null)
    {
        ArgumentNullException.ThrowIfNull(failure);

        if (failure is BrainTestFailureException diagnostic)
        {
            return diagnostic;
        }

        BrainTestArtifact artifact;

        lock (_gate)
        {
            RecordEventLocked(
                operation,
                "failed",
                [("exception.type", failure.GetType().FullName ?? failure.GetType().Name)]);
            artifact = SnapshotLocked(cleanupStage);
        }

        var result = new BrainTestFailureException(
            $"DigitalBrain test framework operation '{Bound(operation)}' failed.",
            artifact,
            failure);

        try
        {
            if (TestContext.Current.Attachments?.ContainsKey(
                BrainTestFailureException.AttachmentName) != true)
            {
                TestContext.Current.AddAttachment(
                    BrainTestFailureException.AttachmentName,
                    artifact.ToJson());
            }
        }
        catch (Exception)
        {
            // The artifact remains available on the exception even when the
            // ambient test framework cannot accept an attachment.
        }

        return result;
    }

    private BrainTestArtifact SnapshotLocked(string? cleanupStage)
    {
        var faults = _faults
            .Select(pair => new BrainTestFault(
                pair.Value.Target,
                pair.Value.State == "armed" && pair.Key.IsConsumed
                    ? "consumed"
                    : pair.Value.State))
            .OrderBy(fault => fault.Target, StringComparer.Ordinal)
            .ThenBy(fault => fault.State, StringComparer.Ordinal)
            .ToArray();

        return BrainTestArtifact.Create(
            _fixtureId,
            _scopeId,
            _moduleIds,
            _owners,
            _clockOrigin,
            _clockUtc,
            _events.Snapshot(),
            faults,
            cleanupStage is null ? null : Bound(cleanupStage));
    }

    private void RecordEventLocked(
        string operation,
        string state,
        IReadOnlyList<(string Key, string Value)> metadata)
    {
        var fields = new Dictionary<string, string>(
            StringComparer.Ordinal);

        foreach (var (key, value) in metadata)
        {
            var boundedKey = Bound(key);
            fields[boundedKey] = Sanitize(key, value);
        }

        _events.Add(new BrainTestEvent(
            ++_sequence,
            Bound(operation),
            Bound(state),
            new ReadOnlyDictionary<string, string>(fields)));
    }

    private string Sanitize(
        string key,
        string value)
        => IsSensitiveKey(key)
            || IsDerivedFromSensitiveOwner(value)
                ? Redacted
                : Bound(value);

    private bool IsDerivedFromSensitiveOwner(string value)
    {
        if (_redactAllDerivedIdentifiers)
        {
            return true;
        }

        var bounded = Bound(value);
        if (_sensitiveOwnerLabels.Contains(bounded))
        {
            return true;
        }

        return _sensitiveOwnerIds.Any(ownerId =>
            value.Contains(ownerId, StringComparison.Ordinal));
    }

    private static bool IsSensitiveKey(string key)
        => SensitiveKeys.Any(fragment =>
            key.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private void TrackSensitiveOwnerLocked(
        string label,
        string ownerId)
    {
        var boundedOwnerId = Bound(ownerId);
        if (_sensitiveOwnerIds.Contains(boundedOwnerId))
        {
            return;
        }

        if (_sensitiveOwnerIds.Count >= BrainTestArtifact.MaximumOwners)
        {
            _redactAllDerivedIdentifiers = true;
            return;
        }

        _sensitiveOwnerIds.Add(boundedOwnerId);
        _sensitiveOwnerLabels.Add(Bound(label));
    }

    private static string Bound(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length <= MaximumStoredStringLength
            ? value
            : value[..MaximumStoredStringLength];
    }

    private sealed record FaultState(
        string Target,
        string State);
}

internal sealed class BoundedRing<T>
{
    private readonly T[] _items;
    private int _count;
    private int _next;

    internal BoundedRing(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _items = new T[capacity];
    }

    internal void Add(T item)
    {
        _items[_next] = item;
        _next = (_next + 1) % _items.Length;
        _count = Math.Min(_count + 1, _items.Length);
    }

    internal IReadOnlyList<T> Snapshot()
    {
        var snapshot = new T[_count];
        var start = (_next - _count + _items.Length) % _items.Length;

        for (var index = 0; index < _count; index++)
        {
            snapshot[index] = _items[(start + index) % _items.Length];
        }

        return Array.AsReadOnly(snapshot);
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrainTestArtifact))]
internal sealed partial class BrainTestJsonContext : JsonSerializerContext;
