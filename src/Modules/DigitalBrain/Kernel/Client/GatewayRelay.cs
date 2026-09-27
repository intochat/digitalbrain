using System.Net;
using System.Net.Sockets;

namespace DigitalBrain.Core;

// Orleans addresses a silo by the IP it advertises. A container cannot use a loopback-advertised
// silo address directly, so it listens on that same loopback endpoint and pipes to the Docker host.
internal sealed class GatewayRelay : IAsyncDisposable
{
    private readonly List<TcpListener> _listeners = [];
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<Task> _accepting = [];

    private GatewayRelay() { }

    public static GatewayRelay Start(IEnumerable<Uri> gateways, string relayHost)
    {
        var relay = new GatewayRelay();
        foreach (var gateway in gateways)
        {
            var listener = new TcpListener(IPAddress.Parse(gateway.Host), gateway.Port);
            listener.Start();
            relay._listeners.Add(listener);
            relay._accepting.Add(relay.AcceptAsync(listener, relayHost, gateway.Port));
        }
        return relay;
    }

    private async Task AcceptAsync(TcpListener listener, string relayHost, int port)
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient inbound;
            try { inbound = await listener.AcceptTcpClientAsync(_stopping.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            _ = PipeAsync(inbound, relayHost, port);
        }
    }

    private async Task PipeAsync(TcpClient inbound, string relayHost, int port)
    {
        using (inbound)
        using (var outbound = new TcpClient { NoDelay = true })
        {
            try
            {
                inbound.NoDelay = true;
                await outbound.ConnectAsync(relayHost, port, _stopping.Token).ConfigureAwait(false);
                var inboundStream = inbound.GetStream();
                var outboundStream = outbound.GetStream();
                await Task.WhenAny(
                    inboundStream.CopyToAsync(outboundStream, _stopping.Token),
                    outboundStream.CopyToAsync(inboundStream, _stopping.Token)).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        foreach (var listener in _listeners) { listener.Stop(); }
        await Task.WhenAll(_accepting).ConfigureAwait(false);
        _stopping.Dispose();
    }
}
