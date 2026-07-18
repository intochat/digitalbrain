using Ino.Core;

namespace Ino.Domains.Reminders.Contracts;

/// <summary>
/// Marker base for everything on the user's reminder journal — both the
/// schedule action (<see cref="ReminderSet"/>) and the eventual fire
/// (<see cref="ReminderDue"/>) live on the same neuron's <see cref="Orleans.Journaling.IDurableList{T}"/>
/// so a plan can ask "what reminders has this user set" and "which ones
/// have fired" with a single <c>IJournaledNeuronQuery&lt;ReminderEvent&gt;</c>
/// call.
///
/// Concrete subtypes carry their own <c>[GenerateSerializer]</c>; this
/// base is abstract and not serialized directly.
/// </summary>
public abstract record ReminderEvent : ISynapse;
