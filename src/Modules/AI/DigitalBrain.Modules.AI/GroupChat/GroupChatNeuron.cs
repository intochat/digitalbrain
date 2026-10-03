using System.Text;
using DigitalBrain;
using DigitalBrain.AI.GroupChat.Signals;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using Orleans.Runtime;

namespace DigitalBrain.AI.GroupChat;

[GrainType("ai.group-chat")]
internal sealed class GroupChatNeuron(
    [PersistentState("ai.group-chat", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<GroupChatState> store)
    : Neuron<GroupChatState>(store), IGroupChat
{
    private const string AgreementMarker = "AGREE";

    private string ChatId => this.GetPrimaryKeyString();

    public async Task<GroupChatState> Configure(GroupChatSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (setup.Participants.Length < 2) { throw new ArgumentException("A group chat needs at least two participants.", nameof(setup)); }
        if (setup.MaxRounds < 1) { throw new ArgumentException("A group chat needs at least one round.", nameof(setup)); }
        foreach (var participant in setup.Participants)
        {
            if (!ModelAddress.IsKnown(participant.Model))
            { throw new ArgumentException($"Unknown model '{participant.Model}' for participant '{participant.Name}'.", nameof(setup)); }
        }
        if (setup.Participants.Select(participant => participant.Name).Distinct(StringComparer.Ordinal).Count() != setup.Participants.Length)
        { throw new ArgumentException("Participant names must be unique.", nameof(setup)); }
        var next = new GroupChatState { Revision = Snapshot.Revision + 1, Setup = setup };
        await Save(next, Changed(next));
        return Snapshot;
    }

    public async Task<GroupChatState> Ask(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var setup = Snapshot.Setup ?? throw new InvalidOperationException("Configure the group chat before asking it.");
        var discussion = new GroupChatState { Revision = Snapshot.Revision + 1, Setup = setup, Question = question, Status = GroupChatStatus.Discussing };
        await Save(discussion, Changed(discussion));
        try
        {
            var agreed = false;
            var round = 1;
            for (; round <= setup.MaxRounds; round++)
            {
                var everyoneAgreed = round > 1;
                foreach (var participant in setup.Participants)
                {
                    var text = await Speak(participant, setup, question, Snapshot.Turns, DiscussionInstruction(round));
                    everyoneAgreed &= text.TrimStart().StartsWith(AgreementMarker, StringComparison.OrdinalIgnoreCase);
                    var turn = new GroupChatTurn(round, participant.Name, text);
                    await Save(Snapshot with { Revision = Snapshot.Revision + 1, Turns = [.. Snapshot.Turns, turn] }, new GroupChatSpoke(ChatId, turn));
                }
                if (everyoneAgreed) { agreed = true; break; }
            }
            var rounds = Math.Min(round, setup.MaxRounds);
            var moderator = setup.Participants[0];
            var answer = await Speak(moderator, setup, question, Snapshot.Turns,
                "The discussion is over. Write the final answer the group converged on, without mentioning the discussion.");
            var concluded = Snapshot with { Revision = Snapshot.Revision + 1, Answer = answer, Agreed = agreed, Status = GroupChatStatus.Concluded };
            await Save(concluded, Changed(concluded), new GroupChatConcluded(ChatId, answer, rounds, agreed));
            return Snapshot;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            var failed = Snapshot with { Revision = Snapshot.Revision + 1, Status = GroupChatStatus.Failed, Failure = error.Message };
            await Save(failed, Changed(failed));
            throw;
        }
    }

    private GroupChatChanged Changed(GroupChatState state) => new(ChatId, state.Revision, state.Status);

    public Task<GroupChatState> Read() => Task.FromResult(Snapshot);

    private static string DiscussionInstruction(int round) => round == 1
        ? "Give your contribution. Build on what the others said before you."
        : $"Build on the discussion so far. If you agree with the current best answer and have nothing to add, start your reply with {AgreementMarker}.";

    private async Task<string> Speak(GroupChatParticipant participant, GroupChatSetup setup, string question,
        IReadOnlyList<GroupChatTurn> transcript, string instruction)
    {
        var others = string.Join(", ", setup.Participants.Where(other => other != participant).Select(other => other.Name));
        var system = new StringBuilder()
            .AppendLine($"You are {participant.Name} in a group discussion with {others}. Keep each turn short.")
            .AppendLine(setup.Brief)
            .AppendLine(participant.Instructions)
            .ToString().Trim();
        var conversation = new StringBuilder().AppendLine($"Question: {question}");
        foreach (var turn in transcript) { conversation.AppendLine($"{turn.Speaker}: {turn.Text}"); }
        conversation.AppendLine().AppendLine(instruction);
        var text = await ModelAddress.Complete(GrainFactory, participant.Model, system, conversation.ToString());
        return text.Length > 0 ? text : throw new InvalidOperationException($"{participant.Name} returned an empty turn.");
    }
}
