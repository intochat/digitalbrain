namespace Brain.Kernel;

public sealed class KindCatalog
{
    private readonly object _gate = new();
    private readonly List<(string Kind, string[] Contracts)> _entries = [];

    public void Add(string kind, string[] contracts)
    {
        lock (_gate)
            _entries.Add((kind, contracts));
    }

    public IReadOnlyList<(string Kind, string[] Contracts)> Entries
    {
        get
        {
            lock (_gate)
                return [.. _entries];
        }
    }
}
