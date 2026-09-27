namespace DigitalBrain.Microsoft.CSharp.Sandbox;

internal sealed class RunLog(int capacity)
{
    private readonly Lock _gate = new();
    private readonly Queue<string> _lines = new();

    public void Append(string line)
    {
        lock (_gate)
        {
            _lines.Enqueue(line);
            if (_lines.Count > capacity) { _lines.Dequeue(); }
        }
    }

    public string Tail(int count)
    {
        lock (_gate)
        {
            return string.Join('\n', _lines.Skip(Math.Max(0, _lines.Count - Math.Clamp(count, 1, capacity))));
        }
    }
}
