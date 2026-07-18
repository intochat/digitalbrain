namespace TripRadar.Server.Infrastructure.Settings;

public class Kafka
{
    public string BootstrapServers { get; set; } = null!;
    public string SaslMechanism { get; set; } = "Plain";
    public string SecurityProtocol { get; set; } = "SaslSsl";
    public string SaslUsername { get; set; } = "$ConnectionString";
    public string SaslPassword { get; set; } = null!;
}
