namespace DigitalBrain.Assistant;

public sealed class IntentActivity
{
    private readonly Dictionary<string, AgentTouchedData> _touched = new(StringComparer.Ordinal);

    public List<AgentCall> Calls { get; } = [];
    public IReadOnlyList<AgentTouchedData> Touched => [.. _touched.Values];

    public void CancelPending()
    {
        for (var i = 0; i < Calls.Count; i++)
        {
            if (Calls[i].ErrorCode == "tool_interrupted")
            { Calls[i] = Calls[i] with { ErrorCode = "tool_cancelled", ErrorMessage = "The tool call was cancelled." }; }
        }
    }

    public void RecordTool(string name, bool succeeded, string? source, long rowsRead,
        string? callId = null, string? errorCode = null, string? errorMessage = null)
    {
        var call = new AgentCall(name, name, false, succeeded, callId, errorCode, errorMessage);
        var existingCall = callId is null ? -1 : Calls.FindIndex(item => item.CallId == callId);
        if (existingCall >= 0) { Calls[existingCall] = call; }
        else { Calls.Add(call); }
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
