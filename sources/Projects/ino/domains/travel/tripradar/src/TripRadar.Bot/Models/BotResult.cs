namespace TripRadar.Bot.Models;

public sealed record BotResult<T>(bool Success, T? Value, string? Error)
{
    public static BotResult<T> Ok(T value) => new(true, value, null);
    public static BotResult<T> Fail(string error) => new(false, default, error);
}
