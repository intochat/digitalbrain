using System.Collections.Concurrent;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.InoLang;
using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Linking;
using DigitalBrain.InoLang.Planning;

namespace DigitalBrain.Kernel.Runtime;

public sealed class InMemoryPlanCache(
    IContractCatalog catalog,
    InoDefinitionCache? definitions = null) : IPlanCache
{
    readonly InoDefinitionCache _definitions = definitions ?? new InoDefinitionCache();
    readonly ConcurrentDictionary<(string Fqn, string InoLangSource), ExecutionPlan> _plans = new();

    public async ValueTask<PlanCacheEntry> GetOrCompileAsync(
        NeuronDescriptor descriptor, CancellationToken ct)
    {
        string source;
        try
        {
            source = await _definitions.GetSourceAsync(descriptor, ct).ConfigureAwait(false);
        }
        catch (InoDefinitionNotRegisteredException ex)
        {
            return RefuseUnavailableDefinition(descriptor.Fqn, ex);
        }
        catch (InvalidDataException ex)
        {
            return RefuseUnavailableDefinition(descriptor.Fqn, ex);
        }
        catch (IOException ex)
        {
            return RefuseUnavailableDefinition(descriptor.Fqn, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return RefuseUnavailableDefinition(descriptor.Fqn, ex);
        }

        var key = (descriptor.Fqn, source);
        if (_plans.TryGetValue(key, out var cached))
            return PlanCacheEntry.Activated(cached, descriptor.Fqn);

        var compiled = InoCompiler.Compile(source, catalog);
        if (!compiled.Success)
        {
            var errors = compiled.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.Message);
            return PlanCacheEntry.Refused(
                descriptor.Fqn,
                $"compilation failed: {string.Join("; ", errors)}");
        }

        var gate = await compiled.EvaluateGateAsync(ct);
        if (!gate.CanActivate)
            return PlanCacheEntry.Refused(descriptor.Fqn, gate.Reason);

        var plan = compiled.Plan!;
        


        _plans[key] = plan;
        return PlanCacheEntry.Activated(plan, descriptor.Fqn);
    }

    static PlanCacheEntry RefuseUnavailableDefinition(string fqn, Exception error) =>
        PlanCacheEntry.Refused(fqn, $"definition unavailable: {error.Message}");
}
