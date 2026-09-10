using System.Collections.Concurrent;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.UI;

namespace DigitalBrain.Tests;

internal sealed class FixtureChatSettlementCrashPoint : IChatSettlementCrashPoint
{
    private readonly ConcurrentDictionary<SignalId, byte> _crashed = new();

    internal bool CrashOnce { get; set; }

    public void BeforeSettlementFire(SignalId turn)
    {
        if (CrashOnce && _crashed.TryAdd(turn, 0))
        {
            throw new InvalidOperationException("simulated loss before the chat settlement was announced");
        }
    }
}
