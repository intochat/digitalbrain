using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

// The chat calls this between saving a terminal turn and announcing it, so a test can lose the
// announcement the way a real storage blip or deactivation would.
public interface IChatSettlementCrashPoint
{
    void BeforeSettlementFire(SignalId turn);
}
