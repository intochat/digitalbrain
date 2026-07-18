using Ino.Core.Hosting;

namespace Ino.Aspire.Hosting;

public static class WithDomainExtensions
{
    public static IInoBuilder WithDomain<T>(this IInoBuilder builder)
        where T : class, IDomain, new()
    {
        var installed = InstalledSet.Load();
        var domain = new T();
        if (installed.Contains(domain.Id))
        {
            Console.Out.WriteLine($"[ino] WithDomain: registering '{domain.Id.Value}' (found in installed.json).");
            builder.RegisterDomain(domain);
        }
        else
        {
            Console.Out.WriteLine($"[ino] WithDomain: skipping '{domain.Id.Value}' — not in installed.json. Install via POST /marketplace/install/{{id}} to enable.");
        }
        return builder;
    }
}
