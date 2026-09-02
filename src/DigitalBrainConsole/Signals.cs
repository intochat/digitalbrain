using DigitalBrain.Abstractions.Signals;

namespace DigitalBrainConsole;

// What the console fires directly at chat:main. Distinct from UserMessageReceived below: if
// ChatNeuron declared IHandle<UserMessageReceived> too, it would be its own broadcast's tier-1
// receiver — a non-reentrant self-Deliver that deadlocks the activation on itself.
[GenerateSerializer]
[Alias("db.console.user-message")]
public sealed record UserMessage(string Text) : Signal;

// What chat:main broadcasts onward. Greeter and logger declare IHandle<UserMessageReceived>;
// ChatNeuron does not, so it never routes back to itself.
[GenerateSerializer]
[Alias("db.console.user-message-received")]
public sealed record UserMessageReceived(string Text) : Signal;
