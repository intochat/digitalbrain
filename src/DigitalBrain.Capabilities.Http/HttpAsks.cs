using DigitalBrain.Units;
using System.Buffers;
using System.Text.Json;

namespace DigitalBrain.Capabilities.Http;

/// <summary>Typed <see cref="Ask"/> builders for an <see cref="IHttpRead"/> slot.</summary>
public static class HttpAsks
{
    /// <summary>Asks for the JSON at <paramref name="url"/>. Repeatable. The key is <paramref name="key"/>, else the url; give a key for a url longer than 128 characters.</summary>
    /// <param name="http">The bound HTTP read slot handle.</param>
    /// <param name="url">The URL to GET.</param>
    /// <param name="key">The correlation key for the ask, or <see langword="null"/> to use <paramref name="url"/>.</param>
    /// <returns>The repeatable ask value.</returns>
    public static Ask GetJson(this Asks<IHttpRead> http, string url, string? key = null) =>
        Get(http, nameof(IHttpRead.GetJson), url, key);

    /// <summary>Asks for the text at <paramref name="url"/>. Repeatable. The key is <paramref name="key"/>, else the url; give a key for a url longer than 128 characters.</summary>
    /// <param name="http">The bound HTTP read slot handle.</param>
    /// <param name="url">The URL to GET.</param>
    /// <param name="key">The correlation key for the ask, or <see langword="null"/> to use <paramref name="url"/>.</param>
    /// <returns>The repeatable ask value.</returns>
    public static Ask GetText(this Asks<IHttpRead> http, string url, string? key = null) =>
        Get(http, nameof(IHttpRead.GetText), url, key);

    private static Ask Get(Asks<IHttpRead> http, string operation, string url, string? key)
    {
        ArgumentNullException.ThrowIfNull(url);
        var buffer = new ArrayBufferWriter<byte>(url.Length + 16);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("url"u8, url);
            writer.WriteEndObject();
            writer.Flush();
        }

        return http.Call(operation, key ?? url, JsonElement.Parse(buffer.WrittenSpan)) with { Repeatable = true };
    }
}
