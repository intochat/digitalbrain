namespace DigitalBrain.Framework.Tests.E2E.MorningStats;

[Capability("sql-read", 1)]
[System.ComponentModel.Description("read-only SQL")]
public interface ISqlRead
{
    Task<T> Scalar<T>(string sql, object? args, CancellationToken ct);
}
