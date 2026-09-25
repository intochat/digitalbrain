using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.CSharpExpert;

// Grain keys stay printable: a solution path has separators and spaces, so runs and profiles key off a
// stable, human-readable id derived from the normalized path instead.
public static class CodingWorkspace
{
    public static string Id(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        var full = Path.GetFullPath(solutionPath);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..12];
        var name = new string(Path.GetFileNameWithoutExtension(full).Where(char.IsAsciiLetterOrDigit).ToArray());
        return $"csharp-expert-{name}-{hash}";
    }
}
