using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Card.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Card;
[GrainType(UIVocabulary.CardType)]
internal sealed class CardNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<CardState> store)
    : Neuron<CardState>(store), ICard
{
    public Task Set(string title, string body, IReadOnlyList<UiChildRef>? children = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(body);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Title = title.Trim();
        next.Body = body;
        next.Children = [.. children ?? []];
        return Save(next, new CardChanged(this.GetPrimaryKeyString(), next.Version));
    }

    [ReadOnly] public Task<CardState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}

