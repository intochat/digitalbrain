using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Swarm;

/// <summary>
/// A thread-safe, in-memory C# workspace that loads code documents,
/// builds a full Roslyn CSharpCompilation, and serves symbol and semantic queries.
/// </summary>
public sealed class SwarmWorkspace
{
    private readonly Dictionary<string, (string Source, SyntaxTree Tree)> _documents = new(StringComparer.Ordinal);
    private CSharpCompilation? _compilation;
    private readonly string _assemblyName;

    public SwarmWorkspace(string assemblyName = "DigitalBrain.Swarm.Dynamic")
    {
        _assemblyName = assemblyName;
    }

    /// <summary>
    /// Gets all document names currently loaded.
    /// </summary>
    public IReadOnlyCollection<string> DocumentNames
    {
        get
        {
            lock (_documents)
            {
                return _documents.Keys.ToList();
            }
        }
    }

    /// <summary>
    /// Add or update an in-memory C# document.
    /// </summary>
    public void AddOrUpdateDocument(string name, string sourceCode)
    {
        lock (_documents)
        {
            var tree = CSharpSyntaxTree.ParseText(sourceCode, path: name);
            _documents[name] = (sourceCode, tree);
            _compilation = null; // Invalidate compilation cache
        }
    }

    /// <summary>
    /// Gets the current in-memory compilation. Builds it if it doesn't exist.
    /// </summary>
    public CSharpCompilation GetCompilation()
    {
        lock (_documents)
        {
            if (_compilation != null)
            {
                return _compilation;
            }

            var trees = _documents.Values.Select(d => d.Tree).ToList();

            // Resolve standard core metadata references
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Runtime.AssemblyTargetedPatchBandAttribute).Assembly.Location),
            };

            // Safely resolve loaded assemblies in the AppDomain to help compilation succeed
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    try
                    {
                        references.Add(MetadataReference.CreateFromFile(assembly.Location));
                    }
                    catch
                    {
                        // Ignore unloadable assemblies
                    }
                }
            }

            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
            
            _compilation = CSharpCompilation.Create(
                _assemblyName,
                trees,
                references.Distinct(MetadataReferenceComparer.Instance),
                options);

            return _compilation;
        }
    }

    /// <summary>
    /// Checks for syntax or semantic compilation diagnostics (warnings & errors).
    /// </summary>
    public ImmutableArray<Diagnostic> GetDiagnostics()
    {
        return GetCompilation().GetDiagnostics();
    }

    /// <summary>
    /// Retrieve type declarations across all documents.
    /// </summary>
    public IReadOnlyList<TypeDeclarationSyntax> GetDeclaredTypes()
    {
        lock (_documents)
        {
            var types = new List<TypeDeclarationSyntax>();
            foreach (var doc in _documents.Values)
            {
                var root = doc.Tree.GetRoot();
                types.AddRange(root.DescendantNodes().OfType<TypeDeclarationSyntax>());
            }
            return types.AsReadOnly();
        }
    }

    /// <summary>
    /// Helper to find public classes/structs/interfaces lacking XML docstrings or comments.
    /// </summary>
    public IReadOnlyList<TypeDeclarationSyntax> FindUndocumentedTypes()
    {
        return GetDeclaredTypes()
            .Where(t => IsPublic(t) && !HasDocComments(t))
            .ToList();
    }

    /// <summary>
    /// Helper to find methods lacking XML docstrings or comments.
    /// </summary>
    public IReadOnlyList<MethodDeclarationSyntax> FindUndocumentedMethods()
    {
        lock (_documents)
        {
            var methods = new List<MethodDeclarationSyntax>();
            foreach (var doc in _documents.Values)
            {
                var root = doc.Tree.GetRoot();
                var decls = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
                foreach (var method in decls)
                {
                    if (IsPublic(method) && !HasDocComments(method))
                    {
                        methods.Add(method);
                    }
                }
            }
            return methods.AsReadOnly();
        }
    }

    private static bool IsPublic(MemberDeclarationSyntax member)
    {
        return member.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
    }

    private static bool HasDocComments(SyntaxNode node)
    {
        var leadingTrivia = node.GetLeadingTrivia();
        return leadingTrivia.Any(t => 
            t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || 
            t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia) ||
            t.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
            t.IsKind(SyntaxKind.MultiLineCommentTrivia));
    }

    private sealed class MetadataReferenceComparer : IEqualityComparer<MetadataReference>
    {
        public static readonly MetadataReferenceComparer Instance = new();

        public bool Equals(MetadataReference? x, MetadataReference? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;
            return string.Equals(x.Display, y.Display, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(MetadataReference obj)
        {
            return obj.Display?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0;
        }
    }
}
