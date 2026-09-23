using DigitalBrain.Apps;

namespace DigitalBrain.Broker;

// What an app actually used while running through the broker, accumulated per app.
public sealed record AppObservation
{
    public required string AppId { get; init; }
    public IReadOnlySet<string> DataClasses { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> Meters { get; init; } = new HashSet<string>();
    public int Calls { get; init; }
}

// Declared-vs-observed diff. Certifying an app to "diff 0" means it used no data class or meter
// that its manifest does not declare; declared permissions it did not use are reported but do not
// fail the diff.
public sealed record DeclaredObservedDiff
{
    public required string AppId { get; init; }
    public IReadOnlyList<string> UndeclaredDataClasses { get; init; } = [];
    public IReadOnlyList<string> UnusedPermissions { get; init; } = [];
    public IReadOnlyList<string> UndeclaredMeters { get; init; } = [];

    public bool IsClean => UndeclaredDataClasses.Count == 0 && UndeclaredMeters.Count == 0;

    public static DeclaredObservedDiff Compute(AppManifest manifest, AppObservation observation)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(observation);
        var declaredDataClasses = manifest.Permissions.Select(permission => permission.SemanticTypeId)
            .ToHashSet(StringComparer.Ordinal);
        var declaredMeters = manifest.Meters.Select(meter => meter.MeterId)
            .ToHashSet(StringComparer.Ordinal);
        return new DeclaredObservedDiff
        {
            AppId = manifest.Id,
            UndeclaredDataClasses = Sorted(observation.DataClasses.Where(used => !declaredDataClasses.Contains(used))),
            UnusedPermissions = Sorted(declaredDataClasses.Where(declared => !observation.DataClasses.Contains(declared))),
            UndeclaredMeters = Sorted(observation.Meters.Where(used => !declaredMeters.Contains(used))),
        };
    }

    private static string[] Sorted(IEnumerable<string> values) =>
        [.. values.OrderBy(value => value, StringComparer.Ordinal)];
}

public interface IAppObservationStore
{
    void Record(string appId, IEnumerable<string> dataClasses, IEnumerable<string> meters);

    AppObservation Observe(string appId);

    DeclaredObservedDiff Diff(AppManifest manifest);
}
