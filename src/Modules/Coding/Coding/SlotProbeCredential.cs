using System.Net.Http.Headers;
using System.Text;
using DigitalBrain.Abstractions.Slots;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Coding;

// The kernel's Basic gate (BasicAuthGate, in the silo that hosts this module) exempts only /health and
// /alive, so a slot probing the other slot's /slots/{slot} or its smoke read has to carry the owner
// credential or read 401 forever. The gate lives in the host, which references this module and not the
// other way round, so the keys come from the contracts assembly both of them do reference.
internal static class SlotProbeCredential
{
    public const string UsernameKey = ActiveSlotNames.AuthUsernameKey;
    public const string PasswordKey = ActiveSlotNames.AuthPasswordKey;

    // Null when the gate is not configured, which is the local, Aspire and test posture.
    public static AuthenticationHeaderValue? Of(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration[UsernameKey] is { Length: > 0 } username && configuration[PasswordKey] is { Length: > 0 } password
            ? new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")))
            : null;
    }
}
