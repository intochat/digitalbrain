namespace DigitalBrain.Aspire.Hosting;

// Registers a module's default hosting projection when AddModule is called.
public interface IDigitalBrainModuleHosting
{
    void Configure(DigitalBrainBuilder brain);
}