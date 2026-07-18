using System.Collections.Concurrent;
using System.Diagnostics;

namespace Ino.ServiceDefaults;

/// <summary>
/// Process-wide capture buffer used by the trace-based E2E fixture. When
/// <see cref="ServiceDefaultsExtensions.TestModeEnvVar"/> is set, every silo
/// in the current process attaches an <see cref="ActivityListener"/> that
/// appends stopped activities into <see cref="Spans"/>. The fixture asserts
/// the cross-silo span chain by filtering on a <c>test.run_id</c> baggage
/// attribute propagated from the harness through every layer.
///
/// Single instance by design — POC E2E tests boot all silos in-process
/// (TestCluster style) so one capture covers every span. If later
/// <c>DistributedApplicationTestingBuilder</c>-based tests spawn silos as
/// separate processes, each process instantiates its own
/// <see cref="InoTestTelemetryCapture"/> and the fixture merges per-process
/// captures before assertion.
///
/// Using <see cref="ActivityListener"/> rather than the OpenTelemetry
/// InMemory exporter: same outcome, BCL-native, captures activities regardless
/// of which OTel exporter chain they traveled, and avoids churn in the
/// exporter package's extension-method shape between OTel SDK versions.
/// </summary>
public sealed class InoTestTelemetryCapture
{
    public static InoTestTelemetryCapture Instance { get; } = new();

    public ConcurrentBag<Activity> Spans { get; } = new();

    public void Clear()
    {
        while (Spans.TryTake(out _)) { }
    }
}
