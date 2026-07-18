using TripRadar.Bot.Notifications.Format;

namespace TripRadar.Bot.Notifications.Tracking;

public interface ITrackingRegistry
{
    void RegisterUser(string username, long chatId);

    bool TryGetChatId(string username, out long chatId);

    bool TryGetSnapshot(string username, ServiceType type, out TrackingSnapshot snapshot);

    void UpsertSnapshot(TrackingSnapshot snapshot);

    void RemoveSnapshot(string username, ServiceType type);
}
