using System.Reflection;

namespace DigitalBrain.Core;

public sealed record ModuleAssemblies(
    IReadOnlyList<Assembly> Contracts,
    IReadOnlyList<Assembly> Implementations);
