namespace DigitalBrain.Aspire.Hosting;

/// <summary>Registers a module's default hosting projection when AddModule is called.</summary>
public interface IDigitalBrainModuleHosting
{
    void Configure(DigitalBrainBuilder brain);
}
