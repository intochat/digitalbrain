using System.Reflection;
using DigitalBrain.VectorMemory;
using Xunit;

namespace DigitalBrain.Memory.Tests;

public sealed class CommunityNotesMemorySample(MemoryFixture fixture)
{
    [Fact(DisplayName =
        "CommunityNotesMemory sample stores and searches a non-reserved community.notes namespace")]
    public async Task Sample_stores_and_searches_community_notes_namespace()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var sample = new CommunityNotesMemory(MemoryFixture.Memory);

        var stored = await sample.StoreNoteAsync(
            test.Client,
            "recipe-1",
            "sourdough starter feed schedule weekly",
            new Dictionary<string, string> { ["kind"] = "note" },
            cancellationToken);

        Assert.True(stored.Stored);
        Assert.Equal(CommunityNotesMemory.Notes, stored.Namespace);
        Assert.Equal("recipe-1", stored.Key);
        Assert.Equal(VectorMemoryStoreStatus.Stored, stored.Status);

        var search = await sample.SearchNotesAsync(
            test.Client,
            "sourdough starter",
            limit: 3,
            cancellationToken: cancellationToken);

        Assert.Equal(CommunityNotesMemory.Notes, search.Namespace);
        var match = Assert.Single(search.Matches);
        Assert.Equal("recipe-1", match.Key);
        Assert.Equal("sourdough starter feed schedule weekly", match.Text);
        Assert.Equal("note", match.Metadata["kind"]);
    }

    [Fact(DisplayName =
        "CommunityNotesMemory sample cannot write the reserved capability namespace")]
    public async Task Sample_cannot_write_reserved_capability_namespace()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var sample = new CommunityNotesMemory(MemoryFixture.Memory);

        var stored = await sample.StoreAsync(
            test.Client,
            VectorMemoryNamespace.Capabilities,
            "forged.capability",
            "community payload must not land here",
            metadata: null,
            cancellationToken);

        Assert.False(stored.Stored);
        Assert.Equal(VectorMemoryNamespace.Capabilities, stored.Namespace);
        Assert.Equal(VectorMemoryStoreStatus.ReservedNamespace, stored.Status);

        var search = await sample.SearchAsync(
            test.Client,
            VectorMemoryNamespace.Capabilities,
            "community payload must not land here",
            limit: 5,
            cancellationToken: cancellationToken);

        Assert.DoesNotContain(search.Matches, match => match.Key == "forged.capability");
    }

    [Fact(DisplayName =
        "CommunityNotesMemory sample assembly references only public Memory contracts — no Qdrant types")]
    public void Sample_assembly_exposes_no_qdrant_and_depends_on_contracts_only()
    {
        var sampleAssembly = typeof(CommunityNotesMemory).Assembly;
        var referencedNames = sampleAssembly.GetReferencedAssemblies()
            .Select(static name => name.Name!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("DigitalBrain.Modules.Memory.Contracts", referencedNames);
        Assert.DoesNotContain("DigitalBrain.Modules.Memory.Qdrant", referencedNames);
        Assert.DoesNotContain("DigitalBrain.Modules.Memory", referencedNames);
        Assert.DoesNotContain("Qdrant.Client", referencedNames);

        foreach (var type in sampleAssembly.GetExportedTypes())
        {
            Assert.DoesNotContain("Qdrant", type.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Embedding", type.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Provider", type.Name, StringComparison.OrdinalIgnoreCase);
        }

        var contractsAssembly = typeof(IVectorMemory).Assembly;
        Assert.Equal(
            "DigitalBrain.Modules.Memory.Contracts",
            contractsAssembly.GetName().Name);

        var sampleUsesOnlyContracts = typeof(CommunityNotesMemory)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(static method => method.GetParameters().Select(static p => p.ParameterType)
                .Append(method.ReturnType)
                .SelectMany(Unwrap))
            .Where(static type => type.Assembly.GetName().Name?.StartsWith("DigitalBrain.Modules.Memory", StringComparison.Ordinal) is true)
            .Select(static type => type.Assembly.GetName().Name!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.All(
            sampleUsesOnlyContracts,
            name => Assert.Equal("DigitalBrain.Modules.Memory.Contracts", name));
    }

    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var nested in Unwrap(argument))
                {
                    yield return nested;
                }
            }
        }
    }
}
