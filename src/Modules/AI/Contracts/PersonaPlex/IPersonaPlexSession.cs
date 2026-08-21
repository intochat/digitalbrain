namespace DigitalBrain.AI.PersonaPlex;

public interface IPersonaPlexSessionFactory
{
    ValueTask<IPersonaPlexSession> CreateAsync(
        PersonaPlexSessionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPersonaPlexSession : IAsyncDisposable
{
    ValueTask<PersonaPlexAudioFrame> ProcessAsync(
        PersonaPlexAudioFrame frame,
        CancellationToken cancellationToken = default);

    ValueTask ResetAsync(CancellationToken cancellationToken = default);
}
