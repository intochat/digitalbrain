using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Core;

// A test seam: the wrapper calls it between staging a command's terminal record and committing it,
// so a test can simulate process loss at the only point where an Attempted command becomes Unknown.
// Nothing registers an implementation outside tests.
internal interface ICommandCrashPoint
{
    void BeforeTerminalPersist(CommandId command);
}
