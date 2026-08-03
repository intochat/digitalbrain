using DigitalBrain.Google;
using Xunit;

namespace DigitalBrain.Tests.Contracts;

public sealed class GoogleNoMcpReference
{
    [Fact(DisplayName =
        "Google runtime assembly references no ModelContextProtocol* package assembly")]
    public void RuntimeAssemblyReferencesNoModelContextProtocol()
    {
        var referenced = typeof(GoogleModule).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain(
            referenced,
            name => name.StartsWith("ModelContextProtocol", StringComparison.Ordinal));
    }
}
