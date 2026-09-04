namespace DigitalBrain.Scripting.Startup;

internal readonly record struct StartupExecutionKey(
    string Owner,
    string ActivationSignalId,
    string ScriptSha256);

internal sealed record StartupExecution
{
    public StartupExecution(
        StartupExecutionKey key,
        bool isSuccess,
        string summary,
        IReadOnlyList<string> diagnostics,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        Key = key;
        IsSuccess = isSuccess;
        Summary = summary;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        CompletedAt = completedAt;
    }

    public StartupExecutionKey Key { get; }

    public bool IsSuccess { get; }

    public string Summary { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    public DateTimeOffset CompletedAt { get; }

    public static StartupExecution Succeeded(
        StartupExecutionKey key,
        string summary,
        DateTimeOffset completedAt)
        => new(key, true, summary, Array.Empty<string>(), completedAt);

    public static StartupExecution Failed(
        StartupExecutionKey key,
        string summary,
        IReadOnlyList<string> diagnostics,
        DateTimeOffset completedAt)
        => new(key, false, summary, diagnostics, completedAt);

    public bool Equals(StartupExecution? other)
        => other is not null
            && Key == other.Key
            && IsSuccess == other.IsSuccess
            && Summary == other.Summary
            && Diagnostics.SequenceEqual(other.Diagnostics)
            && CompletedAt == other.CompletedAt;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Key);
        hash.Add(IsSuccess);
        hash.Add(Summary);
        foreach (var diagnostic in Diagnostics)
        {
            hash.Add(diagnostic);
        }

        hash.Add(CompletedAt);
        return hash.ToHashCode();
    }
}
