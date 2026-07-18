using System.Collections.Concurrent;

namespace TripRadar.Bot.Auth;

public interface IUserSessionStore
{
    bool TryGetByUsername(string username, out UserSession session);
    void Upsert(UserSession session);
    void RemoveByUsername(string username);
}

internal sealed class UserSessionStore : IUserSessionStore
{
    private readonly ConcurrentDictionary<string, UserSession> _byUsername = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetByUsername(string username, out UserSession session)
        => _byUsername.TryGetValue(username, out session!);

    public void Upsert(UserSession session)
        => _byUsername[session.Username] = session;

    public void RemoveByUsername(string username)
        => _byUsername.TryRemove(username, out _);
}
