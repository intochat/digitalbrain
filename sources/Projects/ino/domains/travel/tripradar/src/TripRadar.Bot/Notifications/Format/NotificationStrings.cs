namespace TripRadar.Bot.Notifications.Format;

internal static class NotificationStrings
{
    public const string Header = "Новый результат по сохранённому запросу";
    public const string Cta = "Открой TripRadar, чтобы посмотреть детали.";
    public const string Button = "Открыть в TripRadar";
    public const string WelcomeUnregistered = "Добро пожаловать в TripRadar!\nВойдите, чтобы искать рейсы и получать уведомления о снижении цен.";
    public const string RegisterOnWebsite = "Войти через сайт";
    public const string ContinueWithGoogle = "Войти через Google";
    public const string SignedIn = "✅ Вы вошли в TripRadar.\nНажмите, чтобы начать искать рейсы.";

    public static class TypeLabels
    {
        public const string Flight = "Перелёт";
        public const string Hotel = "Отель";
        public const string LocalPlaces = "Ресторан";
        public const string Event = "Событие";
    }

    public static string LabelFor(ServiceType type) => type switch
    {
        ServiceType.Flight => TypeLabels.Flight,
        ServiceType.Hotel => TypeLabels.Hotel,
        ServiceType.LocalPlaces => TypeLabels.LocalPlaces,
        ServiceType.Event => TypeLabels.Event,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported service type")
    };
}
