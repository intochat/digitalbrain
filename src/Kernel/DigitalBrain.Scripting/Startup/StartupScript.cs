using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Scripting.Startup;

internal sealed record StartupScript(string Path, string Source, string Sha256)
{
    public static StartupScript FromSource(string path, string source)
    {
        var sourceBytes = Encoding.UTF8.GetBytes(source);
        return new StartupScript(path, source, ComputeSha256(sourceBytes));
    }

    public static async Task<StartupScript> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var sourceBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return new StartupScript(path, Encoding.UTF8.GetString(sourceBytes), ComputeSha256(sourceBytes));
    }

    private static string ComputeSha256(ReadOnlySpan<byte> sourceBytes)
        => Convert.ToHexStringLower(SHA256.HashData(sourceBytes));
}
