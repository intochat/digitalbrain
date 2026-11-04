using System.ComponentModel;

namespace DigitalBrain.Tests.Unit.Synapses;

/// <summary>A read-only SQL capability contract used by the slot facts.</summary>
[Capability("sql-read", 1)]
[Description("read-only SQL")]
public interface ISqlRead
{
    Task<T> Scalar<T>(string sql, object? args, CancellationToken ct);
}

/// <summary>A contract without <c>[Capability]</c>.</summary>
public interface IUnmarked;
