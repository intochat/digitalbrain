using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DigitalBrain.Analyzers.Synapses;

namespace DigitalBrain.Runtime.Synapses;

/// <summary>DB0045 on the host side: reads the build's type and member references from the PE bytes (nothing is loaded) against <see cref="BannedApis"/>.</summary>
internal static class BannedReferences
{
    /// <summary>The first banned reference in the assembly, or null when it references none.</summary>
    /// <param name="assembly">The PE image of the build.</param>
    /// <returns>The fully qualified name of the first banned type or member.</returns>
    public static string? First(byte[] assembly)
    {
        using var pe = new PEReader(ImmutableArray.Create(assembly));
        var metadata = pe.GetMetadataReader();

        foreach (var handle in metadata.TypeReferences)
        {
            var type = TypeName(metadata, handle);
            if (BannedApis.IsBannedType(type))
            {
                return type;
            }
        }

        foreach (var handle in metadata.MemberReferences)
        {
            var reference = metadata.GetMemberReference(handle);
            if (Parent(metadata, reference.Parent) is not { } type)
            {
                continue;
            }

            var member = metadata.GetString(reference.Name);
            if (BannedApis.IsBannedMember(type, member))
            {
                return type + "." + member;
            }
        }

        return null;
    }

    private static string? Parent(MetadataReader metadata, EntityHandle parent)
    {
        switch (parent.Kind)
        {
            case HandleKind.TypeReference:
                return TypeName(metadata, (TypeReferenceHandle)parent);
            case HandleKind.TypeSpecification:
                // A generic instantiation: the blob starts GENERICINST (0x15), CLASS|VALUETYPE, then the open type's coded handle.
                var blob = metadata.GetBlobReader(metadata.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
                if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
                {
                    return null;
                }

                blob.ReadSignatureTypeCode();
                var open = blob.ReadTypeHandle();
                return open.Kind == HandleKind.TypeReference ? TypeName(metadata, (TypeReferenceHandle)open) : null;
            default:
                return null;
        }
    }

    // Namespace plus containing types, generic arity dropped: the same shape the analyzer reports.
    private static string TypeName(MetadataReader metadata, TypeReferenceHandle handle)
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
            return TypeName(metadata, (TypeReferenceHandle)reference.ResolutionScope) + "." + name;
        }

        var ns = metadata.GetString(reference.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }
}
