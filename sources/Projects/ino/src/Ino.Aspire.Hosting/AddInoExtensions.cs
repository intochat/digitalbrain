using Aspire.Hosting;

namespace Ino.Aspire.Hosting;

public static class AddInoExtensions
{
    public static IInoBuilder AddIno(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // The IAW substrate (Orleans cluster + Azure blob storage + Qdrant +
        // optional Ollama) is the foundation every ino silo runs on. AddIAW
        // returns an IAWService that ino exposes via InoBuilder.Iaw so silos
        // can chain `.WithReference(ino.Iaw)` to inherit the cluster
        // membership and infra environment block.
        var iaw = builder.AddIAW(name);

        return new InoBuilder(builder, iaw);
    }
}
