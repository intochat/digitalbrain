using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.AI;

internal sealed class Team : GroupChat, ITeam
{
    private const string LineUpName = "ai.team.line-up";

    private readonly IDurableValue<byte[]> _lineUp;
    private readonly Serializer<TeamFormation> _formations;

    public Team()
    {
        _lineUp = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(LineUpName);
        _formations = ServiceProvider.GetRequiredService<Serializer<TeamFormation>>();
    }

    protected override IReadOnlyList<Participant> Participants =>
        [.. LineUp().Models.Select(ParticipantFor)];

    public async Task Form(TeamFormation formation)
    {
        ArgumentNullException.ThrowIfNull(formation);

        var requested = CanonicalLineUp(formation);

        if (LineUpIfFormed() is { } formed)
        {
            if (formed.Models.SequenceEqual(requested.Models, StringComparer.Ordinal))
            {
                return;
            }

            if (HasDurableSession)
            {
                throw new OrchestrationRefusedException(
                    $"Team '{Id}' has already run {Names(formed)} and cannot be re-formed to run {Names(requested)}. Give each line-up its own team name.");
            }
        }

        var previous = _lineUp.Value;
        _lineUp.Value = _formations.SerializeToArray(requested);

        try
        {
            await WriteStateAsync().ConfigureAwait(true);
        }
        catch
        {
            _lineUp.Value = previous;
            throw;
        }
    }

    private Participant ParticipantFor(string model)
    {
        var contract = ModelContracts.Resolve(model);
        var participant = new NeuronId(NeuronId.GrainTypeNameOf(contract), Id.Owner, Id.Name);

        return DigitalBrain.AI.Participant.Of(contract, participant);
    }

    private TeamFormation LineUp()
        => LineUpIfFormed()
            ?? throw new InvalidOperationException(
                $"Team '{Id}' has no line-up. Call {nameof(ITeam)}.{nameof(ITeam.Form)} with the models it should run before asking it to respond.");

    private TeamFormation? LineUpIfFormed()
        => _lineUp.Value is { Length: > 0 } serialized
            ? _formations.Deserialize(serialized)
            : null;

    private static TeamFormation CanonicalLineUp(TeamFormation formation)
        => new(TeamLineUp.Normalized(formation.Models));

    private static string Names(TeamFormation formation) => string.Join(", ", formation.Models);
}
