using Orleans;

namespace Brain.FeasibilityTests.TypedReferences;

public sealed class Gpt56Grain : Grain, IGpt56
{
    public Task<string> GetIdentityAsync() => Task.FromResult(this.GetPrimaryKeyString());
}

public sealed class Grok45Grain : Grain, IGrok45
{
    public Task<string> GetIdentityAsync() => Task.FromResult(this.GetPrimaryKeyString());
}

public sealed class GroupChatGrain : Grain, IGroupChat
{
    private IReadOnlyList<IAgent> _participants = Array.Empty<IAgent>();

    public Task SetParticipantsAsync(IReadOnlyList<IAgent> participants)
    {
        _participants = participants;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<IAgent>> GetParticipantsAsync() => Task.FromResult(_participants);
}
