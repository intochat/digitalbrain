using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

public interface INativeToolContributor
{
    string Name { get; }
    AIFunction Create(IServiceProvider services);
}
