using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace DigitalBrain.AI.PersonaPlex;

internal sealed class RemotePersonaPlexSession : IPersonaPlexSession
{
    internal const int FrameSampleCount = 1920;
    internal const int FrameByteCount = FrameSampleCount * sizeof(short);
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(2);

    private readonly ClientWebSocket _socket;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly Channel<byte[]> _outputs = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(8)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });
    private readonly CancellationTokenSource _receiveCancellation = new();
    private readonly Task _receiveLoop;
    private bool _disposed;
    private bool _receivedFirstFrame;

    internal RemotePersonaPlexSession(ClientWebSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        if (socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("PersonaPlex remote session requires an open adapter stream.");
        }

        _socket = socket;
        _receiveLoop = ReceiveLoopAsync(_receiveCancellation.Token);
    }

    public async ValueTask<PersonaPlexAudioFrame> ProcessAsync(
        PersonaPlexAudioFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            if (_socket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("PersonaPlex adapter stream is closed.");
            }

            var payload = new byte[FrameByteCount];
            WritePcm16LittleEndian(frame.Pcm16.Span, payload);
            await _socket
                .SendAsync(payload, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }

        var timeout = _receivedFirstFrame ? FrameTimeout : FirstFrameTimeout;
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            var received = await _outputs.Reader
                .ReadAsync(timeoutCancellation.Token)
                .ConfigureAwait(false);
            _receivedFirstFrame = true;
            var pcm = new short[FrameSampleCount];
            ReadPcm16LittleEndian(received, pcm);
            return PersonaPlexAudioFrame.Create(frame.Sequence, pcm);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Keep the Kernel/Flutter pipeline moving while moshi catches up.
            return PersonaPlexAudioFrame.Create(frame.Sequence, new short[FrameSampleCount]);
        }
    }

    public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await CloseSocketAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _receiveCancellation.Cancel();
        await CloseSocketAsync().ConfigureAwait(false);
        try
        {
            await _receiveLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }

        _outputs.Writer.TryComplete();
        _receiveCancellation.Dispose();
        _sendGate.Dispose();
        _socket.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[FrameByteCount];
        try
        {
            while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var payload = await ReceiveExactBinaryAsync(buffer, cancellationToken).ConfigureAwait(false);
                await _outputs.Writer.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            _outputs.Writer.TryComplete();
        }
    }

    private async Task CloseSocketAsync()
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await _socket
                    .CloseAsync(WebSocketCloseStatus.NormalClosure, "PersonaPlex session ended.", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
            }
        }
    }

    private async Task<byte[]> ReceiveExactBinaryAsync(byte[] scratch, CancellationToken cancellationToken)
    {
        var count = 0;
        while (true)
        {
            var result = await _socket
                .ReceiveAsync(scratch.AsMemory(count), cancellationToken)
                .ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException("PersonaPlex adapter closed the stream.");
            }

            if (result.MessageType != WebSocketMessageType.Binary)
            {
                throw new InvalidOperationException("PersonaPlex adapter returned a non-binary frame.");
            }

            count += result.Count;
            if (count > FrameByteCount)
            {
                throw new InvalidOperationException("PersonaPlex adapter returned an oversized PCM frame.");
            }

            if (result.EndOfMessage)
            {
                if (count != FrameByteCount)
                {
                    throw new InvalidOperationException("PersonaPlex adapter returned an incomplete PCM frame.");
                }

                var payload = new byte[FrameByteCount];
                Buffer.BlockCopy(scratch, 0, payload, 0, FrameByteCount);
                return payload;
            }
        }
    }

    internal static void WritePcm16LittleEndian(ReadOnlySpan<short> pcm, Span<byte> destination)
    {
        if (pcm.Length != FrameSampleCount || destination.Length < FrameByteCount)
        {
            throw new ArgumentException("PersonaPlex frames require exactly 1920 PCM16 samples.");
        }

        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.AsBytes(pcm).CopyTo(destination);
            return;
        }

        for (var index = 0; index < pcm.Length; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(destination[(index * 2)..], pcm[index]);
        }
    }

    internal static void ReadPcm16LittleEndian(ReadOnlySpan<byte> source, Span<short> pcm)
    {
        if (source.Length != FrameByteCount || pcm.Length != FrameSampleCount)
        {
            throw new ArgumentException("PersonaPlex frames require exactly 3840 PCM16 bytes.");
        }

        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.Cast<byte, short>(source).CopyTo(pcm);
            return;
        }

        for (var index = 0; index < pcm.Length; index++)
        {
            pcm[index] = BinaryPrimitives.ReadInt16LittleEndian(source[(index * 2)..]);
        }
    }
}
