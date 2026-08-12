namespace DigitalBrain.Abstractions;

// Marks a fact that reaches its audience by journal projection rather than by delivery — the
// UI reads chat.responded from the chat's outgoing feed, so resolving zero receivers is normal
// for it and must not be reported as unrouted.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class JournalProjectionAttribute : Attribute;
