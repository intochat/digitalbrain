using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Hosting.Tray;

// Per-user named pipe `\\.\pipe\digitalbrain-<userSid>` that:
//  1. Lets the daemon detect "another instance already running" on startup.
//  2. Forwards second-instance launches (digitalbrain.exe --url ...) into
//     the running daemon so the OS keeps one process per user session.
//
// Per docs/final-simplification/02-WINDOWS-AUTOSTART.md section 7.
internal sealed class SingleInstancePipe : IDisposable
{
    private readonly ILogger<SingleInstancePipe> _logger;
    private readonly UrlSchemeHandler _urlHandler;
    private readonly CancellationTokenSource _cts = new();
    private Task? _serverLoop;

    public SingleInstancePipe(
        ILogger<SingleInstancePipe> logger,
        UrlSchemeHandler urlHandler)
    {
        _logger = logger;
        _urlHandler = urlHandler;
    }

    public static string PipeNameForCurrentUser()
    {
        using var ident = WindowsIdentity.GetCurrent();
        var sid = ident.User?.Value ?? "anonymous";
        return $"digitalbrain-{sid}";
    }

    // True if this process became the server (no other instance running);
    // false if another instance owns the pipe — the caller should then call
    // ForwardAndExit and quit.
    public bool TryAcquireServer()
    {
        try
        {
            _serverLoop = Task.Run(ServerLoopAsync);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start single-instance pipe server.");
            return false;
        }
    }

    // Second-instance entry point: write the URL/command to the running
    // daemon and exit. Returns true on successful forward.
    public static bool TryForward(string payload)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                serverName: ".",
                pipeName: PipeNameForCurrentUser(),
                direction: PipeDirection.Out);
            client.Connect(timeout: 2000);
            var bytes = Encoding.UTF8.GetBytes(payload);
            client.Write(bytes, 0, bytes.Length);
            client.WaitForPipeDrain();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ServerLoopAsync()
    {
        var pipeName = PipeNameForCurrentUser();
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var ms = new MemoryStream();
                await server.CopyToAsync(ms, ct).ConfigureAwait(false);
                var payload = Encoding.UTF8.GetString(ms.ToArray()).Trim();
                if (payload.Length > 0)
                {
                    _logger.LogDebug("Single-instance pipe received: {Payload}", payload);
                    _urlHandler.Dispatch(payload);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Single-instance pipe loop iteration failed.");
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _serverLoop?.Wait(TimeSpan.FromSeconds(1)); } catch { /* shutting down */ }
        _cts.Dispose();
    }
}
