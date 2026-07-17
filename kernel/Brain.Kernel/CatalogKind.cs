using System.Text.Json;
using Brain.Contracts;

namespace Brain.Kernel;

public sealed class CatalogKind(KindCatalog catalog) : INeuronKind
{
    public string Kind => "catalog";
    public string[] Contracts => [];

    public ValueTask<KindResult> InvokeAsync(NeuronContext context, NeuronInvocation invocation) =>
        throw new BrainException(BrainErrors.UnknownContract, invocation.Contract);

    public string Project(NeuronContext context, string projection) =>
        JsonSerializer.Serialize(new
        {
            kinds = catalog.Entries.Select(entry => new { kind = entry.Kind, contracts = entry.Contracts })
        });
}
