using System.Runtime.CompilerServices;
using Aspire.Hosting.Testing;

namespace DigitalBrain.Testing.Generated;

internal static class ModuleBuilderFactory
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IDistributedApplicationTestingBuilder Create()
        => DistributedApplicationTestingBuilder.Create([]);
}
