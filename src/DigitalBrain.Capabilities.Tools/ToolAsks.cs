using DigitalBrain.Units;
using System.Buffers;
using System.Text.Json;

namespace DigitalBrain.Capabilities.Tools;

/// <summary>Typed <see cref="Ask"/> builders for an <see cref="ITool"/> slot.</summary>
public static class ToolAsks
{
    /// <summary>Asks to run <paramref name="tool"/>. Not repeatable unless <paramref name="repeatable"/> says running it twice is harmless.</summary>
    /// <param name="tools">The bound tool slot handle.</param>
    /// <param name="tool">The name of the tool to run.</param>
    /// <param name="args">The JSON arguments object for the tool.</param>
    /// <param name="key">The correlation key for the ask.</param>
    /// <param name="repeatable">True when running the tool twice is harmless and failed attempts may be retried.</param>
    /// <returns>The ask value.</returns>
    public static Ask Run(this Asks<ITool> tools, string tool, JsonElement args, string key, bool repeatable = false)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(key);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("tool"u8, tool);
            if (args.ValueKind == JsonValueKind.Undefined)
            {
                writer.WriteNull("args"u8);
            }
            else
            {
                writer.WritePropertyName("args"u8);
                args.WriteTo(writer);
            }

            writer.WriteEndObject();
            writer.Flush();
        }

        return tools.Call(nameof(ITool.Run), key, JsonElement.Parse(buffer.WrittenSpan)) with { Repeatable = repeatable };
    }
}
