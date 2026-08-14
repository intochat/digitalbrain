using System.Security.Cryptography;
using System.Text;

namespace Brain.Modules.Scheduling;

internal static class ScheduleGrainKey
{
    internal static string Create(string workspace, string scheduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheduleId);
        var workspaceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(workspace)));
        return $"{workspaceHash}:{scheduleId}";
    }
}
