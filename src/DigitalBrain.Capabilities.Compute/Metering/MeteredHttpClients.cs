using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Capabilities.Compute;

public sealed record MeteredHttpClientRegistration(string Name);

public static class MeteredHttpClients
{
    public static IHttpClientBuilder AddMeteredHttpClient(this IServiceCollection services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!services.Any(descriptor => descriptor.ImplementationInstance is MeteredHttpClientRegistration registration && registration.Name == name))
        { services.AddSingleton(new MeteredHttpClientRegistration(name)); }
        // The outermost builder filter runs last after next(), so even an earlier
        // filter's retry handlers stay outside this final attempt boundary.
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IHttpMessageHandlerBuilderFilter)
            && descriptor.ImplementationType == typeof(AttemptFilter)))
        { services.Insert(0, ServiceDescriptor.Singleton<IHttpMessageHandlerBuilderFilter, AttemptFilter>()); }
        return services.AddHttpClient(name);
    }

    private sealed class AttemptFilter : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            if (!builder.Services.GetServices<MeteredHttpClientRegistration>().Any(registration => registration.Name == builder.Name)) { return; }
            switch (builder.PrimaryHandler)
            {
                case HttpClientHandler http: http.AllowAutoRedirect = false; break;
                case SocketsHttpHandler sockets: sockets.AllowAutoRedirect = false; break;
                case ISingleAttemptHttpTransport: break;
                default: throw new InvalidOperationException("A metered custom primary handler must implement ISingleAttemptHttpTransport.");
            }
            // Without a cost meter the call is not charged.
            if (builder.Services.GetService<ICostMeter>() is not { } meter
                || builder.Services.GetService<IExternalCallContextAccessor>() is not { } accessor)
            { return; }
            builder.AdditionalHandlers.Add(new ExternalCallHandler(meter,
                builder.Services.GetRequiredService<ModulePriceListRegistry>(), accessor,
                builder.Services.GetService<TimeProvider>(), builder.Services.GetService<ILogger<ExternalCallHandler>>()));
        };
    }
}
