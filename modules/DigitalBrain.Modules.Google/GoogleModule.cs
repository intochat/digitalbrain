using System.ComponentModel;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Google;

public sealed class GoogleModule : IModule
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<IGoogleMcpAuthorization>(
            _ => new GoogleMcpAuthorization(builder.Configuration));
        builder.Services.AddSingleton<IGmailMcpTransport>(
            _ => new GmailMcpTransport(new HttpClient()));
    }
}
