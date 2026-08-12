using DigitalBrain.Core;
using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Library;

// Content-addressed library hashes (immutable once published).
public static class LibraryContent
{
    public static string Hash(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
