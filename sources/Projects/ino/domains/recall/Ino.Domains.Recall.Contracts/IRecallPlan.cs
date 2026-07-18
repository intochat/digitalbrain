using Ino.Core.Hosting;

namespace Ino.Domains.Recall.Contracts;

/// <summary>
/// Cross-silo plan grain interface for the <c>recall.search</c> neuron.
/// Second IAW→ino bridge neuron (after <c>reminders.set</c>): the plan
/// extracts a question from the user prompt, calls
/// <see cref="IRecallNeuron.LookupAsync"/>, and narrates the hit if any.
/// </summary>
public interface IRecallPlan : INeuronPlan
{
}
