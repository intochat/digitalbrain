using Microsoft.Extensions.Logging;

namespace DigitalBrain.Hosting.Tray;

// Dispatches digitalbrain:// URLs received from a second-instance launch
// (forwarded via SingleInstancePipe). The four routes specified in
// docs/final-simplification/02-WINDOWS-AUTOSTART.md section 7:
//
//   digitalbrain://brain/<id>                       -> switch brain
//   digitalbrain://brain/<id>/intent/<text>         -> open + prefill palette
//   digitalbrain://neuron/<fqn>                     -> camera-land on node
//   digitalbrain://neuron/<fqn>/debug               -> open + debugger panel
//
// V6-1c: routes log; V6-2 wires them to the gRPC gateway calls.
internal sealed class UrlSchemeHandler
{
    public const string Scheme = "digitalbrain";

    private readonly ILogger<UrlSchemeHandler> _logger;

    public UrlSchemeHandler(ILogger<UrlSchemeHandler> logger)
    {
        _logger = logger;
    }

    public void Dispatch(string rawUrl)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Ignoring non-{Scheme} URL: {Url}", Scheme, rawUrl);
            return;
        }

        var host = uri.Host;
        var segments = uri.Segments
            .Select(s => s.Trim('/'))
            .Where(s => s.Length > 0)
            .ToArray();

        if (string.Equals(host, "brain", StringComparison.OrdinalIgnoreCase) &&
            segments.Length >= 1)
        {
            var brainId = Uri.UnescapeDataString(segments[0]);
            if (segments.Length >= 3 &&
                string.Equals(segments[1], "intent", StringComparison.OrdinalIgnoreCase))
            {
                var intent = Uri.UnescapeDataString(segments[2]);
                _logger.LogInformation(
                    "URL route brain/{BrainId}/intent: prefill palette with {Intent}.",
                    brainId, intent);
                return;
            }
            _logger.LogInformation("URL route brain/{BrainId}: switch active brain.", brainId);
            return;
        }

        if (string.Equals(host, "neuron", StringComparison.OrdinalIgnoreCase) &&
            segments.Length >= 1)
        {
            var fqn = Uri.UnescapeDataString(segments[0]);
            var debug = segments.Length >= 2 &&
                string.Equals(segments[1], "debug", StringComparison.OrdinalIgnoreCase);
            _logger.LogInformation(
                "URL route neuron/{Fqn}{Debug}: camera-land on node.",
                fqn, debug ? "/debug" : "");
            return;
        }

        _logger.LogWarning("Unknown {Scheme} URL shape: {Url}", Scheme, rawUrl);
    }
}
