using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using DigitalBrain.Tasks;
using DigitalBrain.Time;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Orleans;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class NeuronContractNamingContracts
{
    private static readonly Assembly[] ContractAssemblies =
    [
        typeof(INeuron).Assembly,
        typeof(IAgent).Assembly,
        typeof(IGmail).Assembly,
        typeof(ISalesforce).Assembly,
        typeof(ITask).Assembly,
        typeof(ICountdown).Assembly,
    ];

    [Fact]
    public void NeuronCapabilityMethodsDoNotEndInAsync()
    {
        var offenders = NeuronContracts()
            .SelectMany(type => type.GetMethods().Select(method => $"{type.FullName}.{method.Name}"))
            .Where(name => name.EndsWith("Async", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryNeuronCapabilityAliasEqualsItsMethodName()
    {
        var offenders = NeuronContracts()
            .SelectMany(type => type.GetMethods())
            .Select(method => new
            {
                Method = $"{method.DeclaringType!.FullName}.{method.Name}",
                Alias = method.GetCustomAttribute<AliasAttribute>()?.Alias,
                method.Name,
            })
            .Where(entry => entry.Alias is null || entry.Alias != entry.Name)
            .Select(entry => $"{entry.Method} alias={entry.Alias ?? "<missing>"}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryNeuronContractAliasIsItsFullyQualifiedTypeName()
    {
        var offenders = NeuronContracts()
            .Select(type => new
            {
                type.FullName,
                Alias = type.GetCustomAttribute<AliasAttribute>()?.Alias,
            })
            .Where(entry => entry.Alias != entry.FullName)
            .Select(entry => $"{entry.FullName} alias={entry.Alias ?? "<missing>"}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NeuronCapabilitySourceUsesNameofAliases()
    {
        var root = RepositoryRoot();
        var sourceRoots = new[]
        {
            Path.Combine(root, "src", "DigitalBrain.Abstractions"),
            Path.Combine(root, "modules"),
        };
        var offenders = sourceRoots
            .SelectMany(sourceRoot => Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            .SelectMany(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path))
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .SelectMany(method => method.AttributeLists
                    .SelectMany(list => list.Attributes)
                    .Where(attribute => attribute.Name.ToString().EndsWith("Alias", StringComparison.Ordinal))
                    .Where(attribute => attribute.ArgumentList?.Arguments.SingleOrDefault()?.Expression
                        is not InvocationExpressionSyntax
                        {
                            Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
                        })
                    .Select(_ => $"{Path.GetRelativePath(root, path)}:{method.GetLocation().GetLineSpan().StartLinePosition.Line + 1}")))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static IEnumerable<Type> NeuronContracts() =>
        ContractAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.IsInterface && type != typeof(INeuron) && typeof(INeuron).IsAssignableFrom(type))
            .Append(typeof(INeuron));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("DigitalBrain.slnx was not found.");
    }
}
