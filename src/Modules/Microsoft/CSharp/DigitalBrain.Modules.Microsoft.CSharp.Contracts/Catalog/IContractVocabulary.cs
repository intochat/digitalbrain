namespace DigitalBrain.Microsoft.CSharp;

public interface IContractVocabulary
{
    ContractVocabularyEntry[] Read();
}

[GenerateSerializer, Alias("csharp.vocabulary-entry")]
public sealed record ContractVocabularyEntry(
    [property: Id(0)] string QualifiedName,
    [property: Id(1)] string Name,
    [property: Id(2)] string Kind,
    [property: Id(3)] string Module,
    [property: Id(4)] string? Description);
