namespace DigitalBrain.Aspire.Hosting;

public interface IDigitalBrainModuleHosting
{
    string Id { get; }
    void Configure(DigitalBrainBuilder brain);
}
