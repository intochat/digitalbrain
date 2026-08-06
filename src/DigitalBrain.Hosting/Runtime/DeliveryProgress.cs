namespace DigitalBrain;

internal sealed record DeliveryProgress(DeliveryTarget[] Pending, int Attempts);
