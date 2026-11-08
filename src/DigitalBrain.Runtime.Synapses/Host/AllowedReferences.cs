using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DigitalBrain.Runtime.Synapses;

/// <summary>
/// The host's boundary for a synapse build: a closed world. Every type and member reference in the PE bytes (nothing is loaded)
/// must be on <see cref="AllowedApis"/>, and the build may declare no P/Invoke. DB0045 in the analyzer is advisory; this is the check.
/// </summary>
internal static class AllowedReferences
{
    /// <summary>The first reference outside the allowed surface, or null when every reference is allowed.</summary>
    /// <param name="assembly">The PE image of the build.</param>
    /// <returns>The fully qualified name of the first disallowed type or member.</returns>
    public static string? FirstDisallowed(byte[] assembly) => Disallowed(assembly).FirstOrDefault();

    /// <summary>Every reference outside the allowed surface, in metadata order.</summary>
    /// <param name="assembly">The PE image of the build.</param>
    /// <returns>The fully qualified names of the disallowed types and members.</returns>
    public static IEnumerable<string> Disallowed(byte[] assembly)
    {
        using var pe = new PEReader(ImmutableArray.Create(assembly));
        var metadata = pe.GetMetadataReader();

        // A P/Invoke (an ImplMap row) is native code whatever names it uses.
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            if (!method.GetImport().Name.IsNil || (method.Attributes & System.Reflection.MethodAttributes.PinvokeImpl) != 0)
            {
                var owner = metadata.GetTypeDefinition(method.GetDeclaringType());
                yield return $"native import {metadata.GetString(owner.Name)}.{metadata.GetString(method.Name)}";
            }
        }

        foreach (var handle in metadata.TypeReferences)
        {
            var (ns, type) = TypeName(metadata, handle);
            if (!AllowedApis.IsAllowedType(ns, type))
            {
                yield return type;
            }
        }

        foreach (var handle in metadata.MemberReferences)
        {
            var reference = metadata.GetMemberReference(handle);
            if (Parent(metadata, reference.Parent) is not { } parent)
            {
                continue;
            }

            var member = metadata.GetString(reference.Name);
            if (!AllowedApis.IsAllowedMember(parent.Namespace, parent.Type, member))
            {
                yield return parent.Type + "." + member;
            }
        }
    }

    private static (string Namespace, string Type)? Parent(MetadataReader metadata, EntityHandle parent)
    {
        switch (parent.Kind)
        {
            case HandleKind.TypeReference:
                return TypeName(metadata, (TypeReferenceHandle)parent);
            case HandleKind.TypeSpecification:
                // A generic instantiation: the blob starts GENERICINST, CLASS|VALUETYPE, then the open type's coded handle.
                var blob = metadata.GetBlobReader(metadata.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
                if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
                {
                    return null;
                }

                blob.ReadSignatureTypeCode();
                var open = blob.ReadTypeHandle();
                return open.Kind == HandleKind.TypeReference ? TypeName(metadata, (TypeReferenceHandle)open) : null;
            default:
                // A TypeDef parent is the build's own type; a MethodDef parent is a vararg call into it.
                return null;
        }
    }

    // The outermost type's namespace and the full name with nested types joined by '.', generic arity dropped.
    private static (string Namespace, string Type) TypeName(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var reference = metadata.GetTypeReference(handle);
        var name = metadata.GetString(reference.Name);
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
        {
            name = name[..tick];
        }

        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var (ns, outer) = TypeName(metadata, (TypeReferenceHandle)reference.ResolutionScope);
            return (ns, outer + "." + name);
        }

        var space = metadata.GetString(reference.Namespace);
        return (space, space.Length == 0 ? name : space + "." + name);
    }
}
