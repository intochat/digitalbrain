namespace DigitalBrain.Hosting.DigitalBrain;

public class DigitalBrainDomainBuilder(DigitalBrainResource digitalbrain)
{
    internal DigitalBrainResource DigitalBrain { get; } = digitalbrain;

    internal virtual void ApplyTo(IResourceBuilder<ProjectResource> silo) { }
}
