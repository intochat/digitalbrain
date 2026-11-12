namespace DigitalBrain.Identity.Local.Directory;

using DigitalBrain.Capabilities.Identity.Model;
using DigitalBrain.Neurons;

/// <summary>Neuron storing active sessions in a <see cref="SessionBook"/>.</summary>
[Neuron(ISessionStore.Type)]
public sealed class SessionsNeuron(TimeProvider? clock = null) : Neuron<SessionBook>, ISessionStore
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task<Session> Start(string tokenHash, string brainId, string principalId, DateTimeOffset expiresAt)
    {
        var (next, session, facts) = State.Start(tokenHash, brainId, principalId, expiresAt, _clock.GetUtcNow());
        await Save(next, facts).ConfigureAwait(true);
        return session;
    }

    /// <inheritdoc />
    public async Task End(string tokenHash)
    {
        var (next, facts) = State.End(tokenHash);
        if (!ReferenceEquals(next, State))
        {
            if (facts.Length > 0)
            {
                await Save(next, facts).ConfigureAwait(true);
            }
            else
            {
                await Store(next).ConfigureAwait(true);
            }
        }
    }

    /// <inheritdoc />
    public Task<Session?> Find(string tokenHash)
        => Task.FromResult(State.Find(tokenHash, _clock.GetUtcNow()));
}
