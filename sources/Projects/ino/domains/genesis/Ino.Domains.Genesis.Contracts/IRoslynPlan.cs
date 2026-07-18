using Ino.Core.Hosting;

namespace Ino.Domains.Genesis.Contracts;

/// <summary>
/// Single shared <see cref="INeuronPlan"/> grain for every dynamic
/// neuron the L1 loop registers. Per-user keyed (Cortex passes the
/// caller's user id), looks up its body from
/// <see cref="INeuronRegistry"/> by
/// <see cref="NeuronPlanContext.NeuronId"/>, and runs it via
/// <c>Microsoft.CodeAnalysis.CSharp.Scripting</c> at execute time.
///
/// Using one pre-registered shell grain sidesteps the open Orleans 10
/// question of resolving runtime-compiled grain classes through the silo
/// manifest — all the dynamism lives in the script body string, not in the
/// grain type itself.
/// </summary>
public interface IRoslynPlan : INeuronPlan
{
}
