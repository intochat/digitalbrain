using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace DigitalBrain.Core;

internal sealed class XmlDocSummaries
{
    private readonly Dictionary<Assembly, XDocument?> _documents = [];

    internal string? For(MethodInfo method)
    {
        var assembly = method.DeclaringType!.Assembly;
        if (!_documents.TryGetValue(assembly, out var document))
        {
            try
            {
                var path = Path.ChangeExtension(assembly.Location, ".xml");
                document = File.Exists(path) ? XDocument.Load(path) : null;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or XmlException or NotSupportedException)
            {
                document = null;
            }

            _documents.Add(assembly, document);
        }

        var parameters = method.GetParameters();
        var id = $"M:{method.DeclaringType.FullName!.Replace('+', '.')}.{method.Name}";
        if (parameters.Length > 0)
        {
            id += $"({string.Join(",", parameters.Select(parameter => TypeName(parameter.ParameterType)))})";
        }

        var summary = document?.Descendants("member").FirstOrDefault(member => (string?)member.Attribute("name") == id)?.Element("summary")?.Value;
        return summary is null ? null : string.Join(" ", summary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string TypeName(Type type)
    {
        if (type.IsArray)
        {
            return TypeName(type.GetElementType()!) + (type.GetArrayRank() == 1 ? "[]" : $"[{string.Join(",", Enumerable.Repeat("0:", type.GetArrayRank()))}]");
        }

        if (type.IsGenericType)
        {
            var name = type.GetGenericTypeDefinition().FullName!;
            return name[..name.IndexOf('`', StringComparison.Ordinal)].Replace('+', '.')
                + "{" + string.Join(",", type.GenericTypeArguments.Select(TypeName)) + "}";
        }

        return type.FullName!.Replace('+', '.');
    }
}
