using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Runtime.InteropServices;

namespace DigitalBrain.AI.PersonaPlex;

internal sealed class RemotePersonaPlexSession : IPersonaPlexSession
{
    internal const int FrameSampleCount = 1920;
    internal const int FrameByteCount = FrameSampleCount * sizeof(short);

    private readonly ClientWebSocket _socket;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly byte[] _receiveBuffer = new byte[FrameByteCount];
    private bool _disposed;

    internal RemotePersonaPlexSession(ClientWebSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        if (socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("PersonaPlex remote session requires an open adapter stream.");
        }

        _socket = socket;
    }

    public async ValueTask<PersonaPlexAudioFrame> ProcessAsync(
        PersonaPlexAudioFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
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

            var received = await ReceiveExactBinaryAsync(cancellationToken).ConfigureAwait(false);
            var pcm = new short[FrameSampleCount];
            ReadPcm16LittleEndian(received, pcm);
            return PersonaPlexAudioFrame.Create(frame.Sequence, pcm);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await CloseSocketAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await CloseSocketAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
            _socket.Dispose();
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

    private async Task<byte[]> ReceiveExactBinaryAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        while (true)
        {
            var result = await _socket
                .ReceiveAsync(_receiveBuffer.AsMemory(count), cancellationToken)
                .ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("PersonaPlex adapter closed the stream.");
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
                Buffer.BlockCopy(_receiveBuffer, 0, payload, 0, FrameByteCount);
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
