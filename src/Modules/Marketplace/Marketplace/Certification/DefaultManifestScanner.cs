using System.Text;
using DigitalBrain.Apps;

namespace DigitalBrain.Marketplace;

// The default scan gate. It rejects an insecure remote endpoint and artifacts that reference a
// banned API. A clean first-party or honest remote manifest produces no findings.
internal sealed class DefaultManifestScanner : IManifestScanner
{
    private static readonly string[] BannedMarkers =
    [
        "System.IO.File",
        "System.IO.Directory",
        "System.Diagnostics.Process",
        "System.Net.Http.HttpClient",
        "System.Net.Sockets.Socket",
        "System.Runtime.InteropServices.Marshal",
    ];

    public IReadOnlyList<ScanFinding> Scan(AppManifest manifest, byte[]? artifact)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var findings = new List<ScanFinding>();
        if (manifest.RemoteEndpoint is { Length: > 0 } endpoint
            && !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new ScanFinding
            {
                Scanner = "network",
                Severity = ScanSeverity.Error,
                Message = "A remote app endpoint must use https.",
            });
        }

        if (artifact is { Length: > 0 } bytes)
        {
            var text = Encoding.UTF8.GetString(bytes);
            foreach (var marker in BannedMarkers)
            {
                if (text.Contains(marker, StringComparison.Ordinal))
                {
                    findings.Add(new ScanFinding
                    {
                        Scanner = "banned-api",
                        Severity = ScanSeverity.Error,
                        Message = $"The artifact references banned API '{marker}'.",
                    });
                }
            }
        }

        return findings;
    }
}
