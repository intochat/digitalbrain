namespace DigitalBrain.Assistant;

// Collected during a turn for the receipt card sent in the agent stream.
public sealed class IntentActivity
{
    private readonly Dictionary<string, AgentTouchedData> _touched = new(StringComparer.Ordinal);

    public List<AgentCall> Calls { get; } = [];
    public IReadOnlyList<AgentTouchedData> Touched => [.. _touched.Values];

    public void RecordTool(string name, bool succeeded, string? source, long rowsRead)
    {
        Calls.Add(new AgentCall(name, name, false, succeeded));
        if (!succeeded)
        {
            return;
        }
        if (rowsRead <= 0 && string.IsNullOrWhiteSpace(source)) { return; }
        var key = string.IsNullOrWhiteSpace(source) ? name : source;
        _touched[key] = _touched.TryGetValue(key, out var existing)
            ? existing with { RowsRead = existing.RowsRead + rowsRead }
            : new AgentTouchedData(key, "table", true, rowsRead);
    }
}
