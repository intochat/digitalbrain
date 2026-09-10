using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Core;

// A test seam: the wrapper calls it before staging a command's terminal record,
// so a test can simulate process loss with only Attempted recorded.
// Nothing registers an implementation outside tests.
internal interface ICommandCrashPoint
{
    void BeforeTerminalRecord(CommandId command);
}
