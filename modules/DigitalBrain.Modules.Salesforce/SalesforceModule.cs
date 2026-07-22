using System.ComponentModel;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Salesforce;

public sealed class SalesforceModule : IModule
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<ISalesforceMcpAuthorization>(
            _ => new SalesforceMcpAuthorization(builder.Configuration));
        builder.Services.AddSingleton<ISalesforceMcpTransport>(
            _ => new SalesforceMcpTransport(new HttpClient()));
    }
}
