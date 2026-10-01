using System.Text.RegularExpressions;

namespace DigitalBrain.Specs;

public sealed class StepContext(string subject, IGrainFactory grains, IServiceProvider services, CancellationToken cancellationToken)
{
    private readonly Dictionary<string, object> _items = new(StringComparer.Ordinal);

    public string Subject { get; } = subject;
    public IGrainFactory Grains { get; } = grains;
    public IServiceProvider Services { get; } = services;
    public CancellationToken CancellationToken { get; } = cancellationToken;

    public void Remember<T>(string name, T value) where T : notnull => _items[name] = value;

    public bool TryRecall<T>(string name, out T value)
    {
        var found = _items.TryGetValue(name, out var item) && item is T;
        value = found ? (T)item! : default!;
        return found;
    }

    public T Recall<T>(string name) => _items.TryGetValue(name, out var value) && value is T typed
        ? typed
        : throw new StepFailedException($"No earlier step produced '{name}'.");
}
