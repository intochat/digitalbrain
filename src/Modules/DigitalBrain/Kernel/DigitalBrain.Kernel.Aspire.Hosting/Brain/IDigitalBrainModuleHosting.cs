namespace DigitalBrain.Aspire.Hosting;

public interface IDigitalBrainModuleHosting
{
    void Configure(DigitalBrainBuilder brain);
}