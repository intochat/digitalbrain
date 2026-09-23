using System.Globalization;

namespace IntoChat.Tests.E2E.Diagnostics;

/// <summary>
/// Minimal OTLP/HTTP protobuf reader for the fields the trace budget and the capture facts need:
/// span identity, name, instrumentation scope, string attributes, and GenAI log bodies.
/// It deliberately ignores everything else on the wire.
/// </summary>
internal static class OtlpTraceParser
{
    public static IReadOnlyList<CapturedSpan> Parse(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var receivedAt = DateTimeOffset.UtcNow;
        var spans = new List<CapturedSpan>();
        foreach (var field in ReadFields(body, 0, body.Length))
        {
            if (field.Number == 1 && field.WireType == 2)
            { ParseResourceSpans(field.Bytes, spans, receivedAt); }
        }

        return spans;
    }

    public static IReadOnlyList<CapturedLog> ParseLogs(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var logs = new List<CapturedLog>();
        foreach (var field in ReadFields(body, 0, body.Length))
        {
            if (field.Number == 1 && field.WireType == 2) { ParseResourceLogs(field.Bytes, logs); }
        }

        return logs;
    }

    private static void ParseResourceLogs(byte[] data, List<CapturedLog> logs)
    {
        foreach (var field in ReadFields(data, 0, data.Length))
        {
            if (field.Number == 2 && field.WireType == 2) { ParseScopeLogs(field.Bytes, logs); }
        }
    }

    private static void ParseScopeLogs(byte[] data, List<CapturedLog> logs)
    {
        foreach (var field in ReadFields(data, 0, data.Length))
        {
            if (field.Number == 2 && field.WireType == 2) { ParseLogRecord(field.Bytes, logs); }
        }
    }

    private static void ParseLogRecord(byte[] data, List<CapturedLog> logs)
    {
        string? body = null;
        IReadOnlyDictionary<string, string> attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in ReadFields(data, 0, data.Length))
        {
            if (field.WireType != 2) { continue; }
            if (field.Number == 5) { body = ParseAnyValue(field.Bytes); }
            else if (field.Number == 6) { attributes = WithAttribute(field.Bytes, attributes); }
        }

        if (body is not null) { logs.Add(new CapturedLog(body, attributes)); }
    }

    private static void ParseResourceSpans(byte[] data, List<CapturedSpan> spans, DateTimeOffset receivedAt)
    {
        foreach (var field in ReadFields(data, 0, data.Length))
        {
            if (field.Number == 2 && field.WireType == 2)
            { ParseScopeSpans(field.Bytes, spans, receivedAt); }
        }
    }

    private static void ParseScopeSpans(byte[] data, List<CapturedSpan> spans, DateTimeOffset receivedAt)
    {
        var scope = string.Empty;
        var rawSpans = new List<byte[]>();
        foreach (var field in ReadFields(data, 0, data.Length))
        {
            if (field.Number == 1 && field.WireType == 2) { scope = ParseScopeName(field.Bytes); }
            else if (field.Number == 2 && field.WireType == 2) { rawSpans.Add(field.Bytes); }
        }

        foreach (var raw in rawSpans)
        {
            if (ParseSpan(raw, scope, receivedAt) is { } span) { spans.Add(span); }
        }
    }

    private static string ParseScopeName(byte[] data)
    {
        foreach (var field in ReadFields(data, 0, data.Length))
        {
            if (field.Number == 1 && field.WireType == 2) { return DecodeString(field.Bytes); }
        }

        return string.Empty;
    }

    private static CapturedSpan? ParseSpan(byte[] data, string scope, DateTimeOffset receivedAt)
    {
        string? name = null;
        byte[]? traceId = null;
        byte[]? spanId = null;
        byte[]? parentId = null;
        ulong kind = 0;
        IReadOnlyDictionary<string, string> attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in ReadFields(data, 0, data.Length))
        {
            if (field.WireType == 0 && field.Number == 6) { kind = field.Varint; continue; }
            if (field.WireType != 2) { continue; }
            switch (field.Number)
            {
                case 1: traceId = field.Bytes; break;
                case 2: spanId = field.Bytes; break;
                case 4: parentId = field.Bytes; break;
                case 5: name = DecodeString(field.Bytes); break;
                case 9: attributes = WithAttribute(field.Bytes, attributes); break;
                default: break;
            }
        }

        if (name is null || traceId is null || spanId is null) { return null; }
        return new CapturedSpan(name, scope, Convert.ToHexStringLower(traceId), Convert.ToHexStringLower(spanId),
            parentId is { Length: > 0 } ? Convert.ToHexStringLower(parentId) : null, (int)kind, receivedAt, attributes);
    }

    // OTLP encodes each repeated KeyValue as its own field occurrence, so one call adds one entry.
    private static IReadOnlyDictionary<string, string> WithAttribute(byte[] keyValue, IReadOnlyDictionary<string, string> existing)
    {
        var attributes = new Dictionary<string, string>(existing, StringComparer.Ordinal);
        string? key = null;
        string? value = null;
        foreach (var field in ReadFields(keyValue, 0, keyValue.Length))
        {
            if (field.WireType != 2) { continue; }
            if (field.Number == 1) { key = DecodeString(field.Bytes); }
            else if (field.Number == 2) { value = ParseAnyValue(field.Bytes); }
        }

        if (key is not null && value is not null) { attributes[key] = value; }
        return attributes;
    }

    private static string? ParseAnyValue(byte[] data)
    {
        foreach (var field in ReadFields(data, 0, data.Length))
        {
            if (field.WireType == 2 && field.Number == 1) { return DecodeString(field.Bytes); }
            if (field.WireType == 0 && field.Number == 2) { return field.Varint != 0 ? "true" : "false"; }
            if (field.WireType == 0 && field.Number == 3) { return ((long)field.Varint).ToString(CultureInfo.InvariantCulture); }
            if (field.WireType == 1 && field.Number == 4) { return BitConverter.ToDouble(field.Bytes).ToString(CultureInfo.InvariantCulture); }
            if (field.WireType == 2 && field.Number == 5)
            {
                var parts = new List<string>();
                foreach (var item in ReadFields(field.Bytes, 0, field.Bytes.Length))
                {
                    if (item.Number != 1 || item.WireType != 2) { continue; }
                    if (ParseAnyValue(item.Bytes) is { } value) { parts.Add(value); }
                }

                return "[" + string.Join(",", parts) + "]";
            }
        }

        return null;
    }

    private static string DecodeString(byte[] bytes) => System.Text.Encoding.UTF8.GetString(bytes);

    private static IEnumerable<ProtoField> ReadFields(byte[] data, int start, int end)
    {
        var position = start;
        while (position < end)
        {
            if (!TryReadVarint(data, ref position, out var tag)) { yield break; }
            var number = (int)(tag >> 3);
            var wireType = (int)(tag & 7);
            switch (wireType)
            {
                case 0:
                    if (!TryReadVarint(data, ref position, out var varint)) { yield break; }
                    yield return new ProtoField(number, wireType, [], varint);
                    break;
                case 1:
                    if (position + 8 > end) { yield break; }
                    yield return new ProtoField(number, wireType, data[position..(position + 8)], 0);
                    position += 8;
                    break;
                case 2:
                    if (!TryReadVarint(data, ref position, out var length) || length > (ulong)(end - position)) { yield break; }
                    var count = (int)length;
                    yield return new ProtoField(number, wireType, data[position..(position + count)], 0);
                    position += count;
                    break;
                case 5:
                    if (position + 4 > end) { yield break; }
                    yield return new ProtoField(number, wireType, data[position..(position + 4)], 0);
                    position += 4;
                    break;
                default:
                    yield break;
            }
        }
    }

    private static bool TryReadVarint(byte[] data, ref int position, out ulong value)
    {
        value = 0;
        var shift = 0;
        while (position < data.Length)
        {
            var current = data[position++];
            value |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0) { return true; }
            shift += 7;
            if (shift >= 64) { return false; }
        }

        return false;
    }

    private readonly record struct ProtoField(int Number, int WireType, byte[] Bytes, ulong Varint);
}

internal sealed record CapturedSpan(string Name, string Scope, string TraceId, string SpanId, string? ParentSpanId,
    int Kind, DateTimeOffset ReceivedAt, IReadOnlyDictionary<string, string> Attributes);

internal sealed record CapturedLog(string Body, IReadOnlyDictionary<string, string> Attributes);