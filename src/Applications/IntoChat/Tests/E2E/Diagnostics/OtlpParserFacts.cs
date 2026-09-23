using System.Text;

namespace IntoChat.Tests.E2E.Diagnostics;

/// <summary>
/// Guards the minimal OTLP reader used by the trace budget and capture facts: the fields those
/// facts depend on (name, span identity, string attributes, log body) must survive the wire shape.
/// </summary>
public sealed class OtlpParserFacts
{
    [Fact]
    public void StringAttributesAndLogBodiesAreReadFromTheExport()
    {
        var span = Span("chat gpt-5.6-luna", ("gen_ai.input.messages", "Show active leads"));
        var trace = Field(1, ResourceSpans(ScopeSpans(span)));
        var parsed = Assert.Single(OtlpTraceParser.Parse(trace));
        Assert.Equal("chat gpt-5.6-luna", parsed.Name);
        Assert.Equal("Show active leads", parsed.Attributes["gen_ai.input.messages"]);

        var logRecord = Concat(Field(5, AnyValueString("My personal note is canary")), Field(6, KeyValue("log.scope", "genai")));
        var logs = Field(1, ResourceLogs(ScopeLogs(logRecord)));
        var log = Assert.Single(OtlpTraceParser.ParseLogs(logs));
        Assert.Equal("My personal note is canary", log.Body);
        Assert.Equal("genai", log.Attributes["log.scope"]);
    }

    private static byte[] Span(string name, params (string Key, string Value)[] attributes)
        => Concat(
            [
                Field(1, Bytes([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16])),
                Field(2, Bytes([1, 2, 3, 4, 5, 6, 7, 8])),
                Field(5, String(name)),
                .. attributes.Select(attribute => Field(9, KeyValue(attribute.Key, attribute.Value))),
            ]);

    private static byte[] ResourceSpans(byte[] scopeSpans) => Field(2, scopeSpans);
    private static byte[] ScopeSpans(byte[] spans) => Field(2, spans);
    private static byte[] ResourceLogs(byte[] scopeLogs) => Field(2, scopeLogs);
    private static byte[] ScopeLogs(byte[] records) => Field(2, records);

    private static byte[] KeyValue(string key, string value)
        => Concat(Field(1, String(key)), Field(2, AnyValueString(value)));

    private static byte[] AnyValueString(string value) => Field(1, String(value));

    private static byte[] Field(int number, byte[] value)
        => Concat(EncodeVarint(((ulong)number << 3) | 2), EncodeVarint((ulong)value.Length), value);

    private static byte[] String(string value) => Encoding.UTF8.GetBytes(value);
    private static byte[] Bytes(byte[] value) => value;

    private static byte[] EncodeVarint(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            var current = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) { current |= 0x80; }
            bytes.Add(current);
        }
        while (value != 0);
        return bytes.ToArray();
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(part => part.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }
}