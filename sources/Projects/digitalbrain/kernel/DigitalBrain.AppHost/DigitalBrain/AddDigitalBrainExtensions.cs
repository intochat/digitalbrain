namespace DigitalBrain.Hosting.DigitalBrain;

public static class AddDigitalBrainExtensions
{
    public static DigitalBrainResource AddDigitalBrain(
        this IDistributedApplicationBuilder builder, string name = "digitalbrain")
    {
        var digitalbrain = new DigitalBrainResource(name, builder);
        digitalbrain.InitializeInfrastructure();
        return digitalbrain;
    }
}
