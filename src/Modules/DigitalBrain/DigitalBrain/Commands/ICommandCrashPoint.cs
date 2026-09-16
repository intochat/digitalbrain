using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Core;

// Test seam called before the terminal command record is staged.
// Nothing registers an implementation outside tests.
internal interface ICommandCrashPoint
{
    void BeforeTerminalRecord(CommandId command);
}
