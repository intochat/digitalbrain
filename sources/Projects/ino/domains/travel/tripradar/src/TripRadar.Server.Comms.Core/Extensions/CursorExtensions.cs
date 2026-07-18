namespace TripRadar.Server.Comms.Core.Extensions;

public static class CursorExtensions
{
    public static bool TryDecodeCursor(string cursor, out string? value)
    {
        value = null;

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            var padding = 4 - (base64.Length % 4);
            if (padding is > 0 and < 4)
            {
                base64 = base64.PadRight(base64.Length + padding, '=');
            }

            var bytes = Convert.FromBase64String(base64);
            value = System.Text.Encoding.UTF8.GetString(bytes);
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static string EncodeCursor(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        var base64 = Convert.ToBase64String(bytes);
        return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}