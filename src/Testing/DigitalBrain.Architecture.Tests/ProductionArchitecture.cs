using System.Reflection;
using System.Runtime.CompilerServices;
using ArchUnitNET.Loader;
using Type = System.Type;

namespace DigitalBrain.Architecture.Tests;

internal static class ProductionArchitecture
{
    internal static readonly System.Reflection.Assembly[] Assemblies = File.ReadAllLines(
        Path.Combine(AppContext.BaseDirectory, "ArchitectureAssemblies.txt"))
        .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct()
        .Select(System.Reflection.Assembly.Load).ToArray();

    internal static readonly Type[] Types = Assemblies.SelectMany(assembly => assembly.GetTypes())
        .Where(type => type.Namespace?.StartsWith("OrleansCodeGen", StringComparison.Ordinal) != true
            && !type.IsDefined(typeof(CompilerGeneratedAttribute), false)).ToArray();

    internal static readonly Lazy<ArchUnitNET.Domain.Architecture> Graph = new(() =>
        new ArchLoader().LoadAssemblies(Assemblies).Build());
}
