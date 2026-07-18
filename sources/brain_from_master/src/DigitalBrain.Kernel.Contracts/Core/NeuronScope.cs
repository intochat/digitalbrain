namespace DigitalBrain.Kernel.Contracts;

public readonly record struct NeuronScope(UserId UserId, string? ThreadId)
{
    public static bool TryParse(string grainKey, out NeuronScope scope)
    {
        if (string.IsNullOrWhiteSpace(grainKey))
        {
            scope = default;
            return false;
        }
        var separatorIndex = grainKey.IndexOf('/');
        scope = separatorIndex < 0
            ? new NeuronScope(new UserId(grainKey), null)
            : new NeuronScope(new UserId(grainKey[..separatorIndex]), grainKey[(separatorIndex + 1)..]);
        return true;
    }
    public string ToKey() => ThreadId is null ? UserId.Value : $"{UserId.Value}/{ThreadId}";
}
public static class IntegrationConfigScopes
{
    public const string App = "default";
    public static string ForUser(UserId userId) => $"user:{userId.Value}";
}
