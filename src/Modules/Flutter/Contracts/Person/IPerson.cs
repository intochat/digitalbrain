using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Person;

[Alias("person"), Orleans.Metadata.DefaultGrainType(UIVocabulary.PersonType)]
public interface IPerson : INeuron
{
    Task Set(string displayName, string avatarUrl);
    [ReadOnly, Alias("read")] Task<PersonState> Read();
}

[GenerateSerializer, Alias("ui.person-state")]
public sealed class PersonState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public int Version { get; set; }
    [Id(2)] public string DisplayName { get; set; } = "";
    [Id(3)] public string AvatarUrl { get; set; } = "";
}