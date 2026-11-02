using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Contracts;

/// <summary>
/// DB0033: Detects synchronous contract call cycles between neurons that will deadlock actor turns.
/// Differentiates synchronous blocking calls from asynchronous [OneWayCall] notifications and signal publishing.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CallCycleAnalyzer : DiagnosticAnalyzer
{
    private const string OneWayCallAttribute = "DigitalBrain.OneWayCallAttribute";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.CallCycle);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(compilationContext =>
        {
            var edges = new List<CallEdge>();
            var neuronTypes = new List<INamedTypeSymbol>();
            var interfaceToNeuron = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
            var collected = false;

            compilationContext.RegisterSemanticModelAction(smContext =>
            {
                lock (edges)
                {
                    if (!collected)
                    {
                        CollectNeuronTypes(smContext.SemanticModel.Compilation.GlobalNamespace, neuronTypes);
                        foreach (var neuron in neuronTypes)
                        {
                            foreach (var iface in neuron.AllInterfaces)
                            {
                                if (!interfaceToNeuron.TryGetValue(iface, out var list))
                                {
                                    list = new List<INamedTypeSymbol>();
                                    interfaceToNeuron[iface] = list;
                                }
                                list.Add(neuron);
                            }
                        }

                        collected = true;
                    }

                    var root = smContext.SemanticModel.SyntaxTree.GetRoot(smContext.CancellationToken);
                    var awaitExpressions = root.DescendantNodes().OfType<AwaitExpressionSyntax>();
                    foreach (var awaitExpr in awaitExpressions)
                    {
                        var enclosingClass = awaitExpr.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                        if (enclosingClass == null)
                        {
                            continue;
                        }

                        if (smContext.SemanticModel.GetDeclaredSymbol(enclosingClass, smContext.CancellationToken) is not INamedTypeSymbol neuron)
                        {
                            continue;
                        }

                        if (!neuronTypes.Contains(neuron, SymbolEqualityComparer.Default))
                        {
                            continue;
                        }

                        var invocations = awaitExpr.DescendantNodes().OfType<InvocationExpressionSyntax>();
                        foreach (var inv in invocations)
                        {
                            if (smContext.SemanticModel.GetSymbolInfo(inv, smContext.CancellationToken).Symbol is not IMethodSymbol targetMethod)
                            {
                                continue;
                            }

                            if (targetMethod.Name == "Publish" || targetMethod.Name == "Save")
                            {
                                continue;
                            }

                            if (Rules.Has(targetMethod, OneWayCallAttribute))
                            {
                                continue;
                            }

                            var targetType = targetMethod.ContainingType;
                            if (targetType == null)
                            {
                                continue;
                            }

                            if (targetType.TypeKind == TypeKind.Interface && interfaceToNeuron.TryGetValue(targetType, out var targets))
                            {
                                foreach (var target in targets)
                                {
                                    if (!SymbolEqualityComparer.Default.Equals(neuron, target))
                                    {
                                        edges.Add(new CallEdge
                                        {
                                            From = neuron,
                                            To = target,
                                            CallLocation = inv.GetLocation()
                                        });
                                    }
                                }
                            }
                            else if (neuronTypes.Contains(targetType, SymbolEqualityComparer.Default) &&
                                     !SymbolEqualityComparer.Default.Equals(neuron, targetType))
                            {
                                edges.Add(new CallEdge
                                {
                                    From = neuron,
                                    To = targetType,
                                    CallLocation = inv.GetLocation()
                                });
                            }
                        }
                    }
                }
            });

            compilationContext.RegisterCompilationEndAction(endContext =>
            {
                lock (edges)
                {
                    foreach (var edge in edges)
                    {
                        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                        var path = new List<INamedTypeSymbol> { edge.From };

                        if (HasPath(edge.To, edge.From, edges, visited, path))
                        {
                            var cycleStr = string.Join(" -> ", path.Select(p => p.Name).Concat(new[] { edge.From.Name }).ToArray());
                            endContext.ReportDiagnostic(Diagnostic.Create(Rules.CallCycle, edge.CallLocation, cycleStr));
                        }
                    }
                }
            });
        });
    }

    private sealed class CallEdge
    {
        public INamedTypeSymbol From { get; set; } = null!;
        public INamedTypeSymbol To { get; set; } = null!;
        public Location CallLocation { get; set; } = null!;
    }

    private static bool HasPath(
        INamedTypeSymbol current,
        INamedTypeSymbol target,
        List<CallEdge> edges,
        HashSet<INamedTypeSymbol> visited,
        List<INamedTypeSymbol> path)
    {
        if (SymbolEqualityComparer.Default.Equals(current, target))
        {
            return true;
        }

        if (!visited.Add(current))
        {
            return false;
        }

        path.Add(current);

        foreach (var edge in edges)
        {
            if (SymbolEqualityComparer.Default.Equals(edge.From, current))
            {
                if (HasPath(edge.To, target, edges, visited, path))
                {
                    return true;
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    private static void CollectNeuronTypes(INamespaceOrTypeSymbol container, List<INamedTypeSymbol> neuronTypes)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol childNs)
            {
                CollectNeuronTypes(childNs, neuronTypes);
            }
            else if (member is INamedTypeSymbol type)
            {
                if (type.TypeKind == TypeKind.Class &&
                    (Rules.DerivesFrom(type, Rules.NeuronType) || Rules.Has(type, "DigitalBrain.NeuronAttribute")))
                {
                    neuronTypes.Add(type);
                }
                CollectNeuronTypes(type, neuronTypes);
            }
        }
    }
}
