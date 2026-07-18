using System.Collections.Concurrent;
using TripRadar.Bot.Notifications.Format;

namespace TripRadar.Bot.Notifications.Tracking;

public sealed class TrackingRegistry : ITrackingRegistry
{
    private readonly ConcurrentDictionary<string, long> _chatByUsername = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<SnapshotKey, TrackingSnapshot> _snapshots = new();

    public void RegisterUser(string username, long chatId)
    {
        if (string.IsNullOrWhiteSpace(username) || chatId <= 0)
            return;

        _chatByUsername[username] = chatId;
    }

    public bool TryGetChatId(string username, out long chatId)
        => _chatByUsername.TryGetValue(username, out chatId);

    public bool TryGetSnapshot(string username, ServiceType type, out TrackingSnapshot snapshot)
        => _snapshots.TryGetValue(new SnapshotKey(username, type), out snapshot!);

    public void UpsertSnapshot(TrackingSnapshot snapshot)
    {
        var key = new SnapshotKey(snapshot.Username, snapshot.ServiceType);
        _snapshots[key] = snapshot;
        _chatByUsername[snapshot.Username] = snapshot.ChatId;
    }

    public void RemoveSnapshot(string username, ServiceType type)
        => _snapshots.TryRemove(new SnapshotKey(username, type), out _);

    private readonly record struct SnapshotKey(string Username, ServiceType Type)
    {
        public bool Equals(SnapshotKey other)
            => Type == other.Type
               && string.Equals(Username, other.Username, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode()
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Username), (int)Type);
    }
}
