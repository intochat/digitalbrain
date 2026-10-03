using System.Text.RegularExpressions;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Person.Signals;
using DigitalBrain.Kernel;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Person;

[GrainType(UIVocabulary.PersonType)]
internal sealed class PersonNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<PersonState> store)
    : Neuron<PersonState>(store), IPerson
{
    public Task Set(string displayName, string avatarUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(avatarUrl);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.DisplayName = displayName.Trim();
        next.AvatarUrl = avatarUrl.Trim();
        return Save(next, new PersonChanged(this.GetPrimaryKeyString(), next.Version));
    }

    [ReadOnly] public Task<PersonState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}
