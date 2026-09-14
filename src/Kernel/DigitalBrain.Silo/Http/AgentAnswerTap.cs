using System.Text;
using System.Text.Json;

namespace DigitalBrain.Kernel;

// A write-through stream that reads the assistant's text out of the AG-UI frames it forwards. Framing is
// done on bytes, because a chunk boundary can fall inside a UTF-8 rune.
internal sealed class AgentAnswerTap(Stream inner, AgentRunLedger ledger, string runId) : Stream
{
    private readonly StringBuilder _answer = new();
    private readonly List<byte> _line = [];
    private bool _finished;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        Observe(new ReadOnlySpan<byte>(buffer, offset, count));
        inner.Write(buffer, offset, count);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Observe(buffer.Span);
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    internal void Complete()
    {
        if (_finished)
        {
            ledger.Record(runId, _answer.ToString());
        }
    }

    // The response body belongs to the pipeline, not to this wrapper.
    protected override void Dispose(bool disposing)
    {
    }

    private void Observe(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value == (byte)'\n')
            {
                ReadLine();
                _line.Clear();
            }
            else if (value != (byte)'\r')
            {
                _line.Add(value);
            }
        }
    }

    private void ReadLine()
    {
        const string prefix = "data:";
        if (_line.Count <= prefix.Length)
        {
            return;
        }

        var text = Encoding.UTF8.GetString([.. _line]);
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            using var frame = JsonDocument.Parse(text[prefix.Length..]);
            if (!frame.RootElement.TryGetProperty("type", out var type))
            {
                return;
            }

            switch (type.GetString())
            {
                case "TEXT_MESSAGE_CONTENT" when frame.RootElement.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.String:
                    _answer.Append(delta.GetString());
                    break;
                case "RUN_FINISHED":
                    _finished = true;
                    break;
                default:
                    break;
            }
        }
        catch (JsonException)
        {
            // A keep-alive comment or a frame this kernel does not model: nothing to record.
        }
    }
}
