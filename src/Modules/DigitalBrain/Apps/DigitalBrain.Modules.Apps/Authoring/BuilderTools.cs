using DigitalBrain.AI;
using DigitalBrain.Registry;

namespace DigitalBrain.Apps;

// The Builder's grounding: search the registry for neuron contracts, read a module's contracts in
// full, and compile-check C# before answering. The registry is the catalog; nothing here reflects.
internal static class BuilderTools
{
    public const string SearchContracts = "search_contracts";
    public const string ReadContracts = "read_contracts";
    public const string CheckCSharp = "check_csharp";

    public static readonly IReadOnlyList<InferenceTool> Definitions =
    [
        new(SearchContracts, "Search this brain's neuron contracts by what they do (for example \"schedule a timer\" or \"send an email\"). Returns matching contracts with their module id.",
            """{"type":"object","properties":{"query":{"type":"string"}},"required":["query"]}"""),
        new(ReadContracts, "Read the full contracts of the given module ids: every neuron interface with its methods and signals, and the #:project directive a C# file needs to use them. Pass modules=[] to list the installed module ids.",
            """{"type":"object","properties":{"modules":{"type":"array","items":{"type":"string"}}},"required":["modules"]}"""),
        new(CheckCSharp, "Compile the given C# files (path to full source, for example tests.cs and behaviors/notify.cs) without running them. Returns the compile errors; fix them all before answering.",
            """{"type":"object","properties":{"files":{"type":"object","additionalProperties":{"type":"string"}}},"required":["files"]}"""),
    ];

    public sealed record SearchArguments(string Query);
    public sealed record ReadArguments(IReadOnlyList<string>? Modules);
    public sealed record CheckArguments(IReadOnlyDictionary<string, string>? Files);

    public sealed record ContractHit(string Module, string Contract, string Description);

    public static async Task<IReadOnlyList<ContractHit>> Search(IGrainFactory grains, string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var registry = grains.GetGrain<IRegistry>(IRegistry.Key);
        try
        {
            return [.. (await registry.Search(query)).Select(hit => Hit(hit.Type))];
        }
        // Without an embedding model the registry has no vector search; a name and description
        // match over the full catalog still answers the Builder's question.
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return [.. (await registry.Types())
                .Where(type => query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word =>
                    type.Id.Contains(word, StringComparison.OrdinalIgnoreCase)
                    || type.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
                    || type.Description.Contains(word, StringComparison.OrdinalIgnoreCase)))
                .Take(10)
                .Select(Hit)];
        }
    }

    private static ContractHit Hit(NeuronType type) => new(type.ModuleId, type.Contract, type.Description);
}

