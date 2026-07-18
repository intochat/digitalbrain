namespace Ino.Core;

/// <summary>
/// Marks an ISynapse record as a user-invocable intent reachable from natural-language
/// input. Indexed at install time into the system silo's intent classifier (Phase 4).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UserEntryAttribute : Attribute
{
}
