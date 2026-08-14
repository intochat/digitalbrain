using System.Security.Cryptography;
using System.Text;

namespace Brain.Modules.Memory;

internal static class MemoryGrainKey
{
    internal static string Create(string workspace, string memoryNamespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryNamespace);
        var workspaceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(workspace)));
        return $"{workspaceHash}:{memoryNamespace}";
    }
}
