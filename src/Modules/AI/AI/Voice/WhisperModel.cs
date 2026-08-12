namespace DigitalBrain.AI;

public abstract class WhisperModel
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract int Priority { get; }

    private static readonly Lazy<IReadOnlyList<WhisperModel>> AllLazy = new(static () =>
        [.. typeof(WhisperModel).Assembly.GetTypes()
            .Where(static t => t is { IsClass: true, IsAbstract: false } && t.IsSubclassOf(typeof(WhisperModel)))
            .Select(static t => (WhisperModel)Activator.CreateInstance(t)!)
            .OrderByDescending(static m => m.Priority)]);

    public static IReadOnlyList<WhisperModel> All => AllLazy.Value;

    public static WhisperModel? FindById(string id)
        => All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    public static WhisperModel? FindByMarker(Type marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        var name = marker.Name;
        if (name.StartsWith('I') && name.Length > 1)
        {
            name = name[1..];
        }

        return All.FirstOrDefault(m => string.Equals(m.GetType().Name, name, StringComparison.Ordinal));
    }
}

public sealed class WhisperTiny : WhisperModel
{
    public override string Id => "whisper-tiny";
    public override string DisplayName => "Whisper Tiny";
    public override int Priority => 10;
}

public sealed class WhisperSmall : WhisperModel
{
    public override string Id => "whisper-small";
    public override string DisplayName => "Whisper Small";
    public override int Priority => 50;
}

public sealed class WhisperLargeV3Turbo : WhisperModel
{
    public override string Id => "whisper-large-v3-turbo";
    public override string DisplayName => "Whisper Large V3 Turbo";
    public override int Priority => 100;
}
