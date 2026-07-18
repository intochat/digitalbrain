using Brain.Mcp;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);
builder.UseOrleansClient(client =>
{
    if (builder.Configuration.GetValue("Orleans:UseLocalhostClustering", false))
    {
        client.UseLocalhostClustering(
            clusterId: builder.Configuration["Orleans:ClusterId"] ?? "dev",
            serviceId: builder.Configuration["Orleans:ServiceId"] ?? "dev");
    }
});
builder.Services.AddSingleton<ITypedCommandPath, JournalOutboxFeedCommandPath>();
builder.Services.AddSingleton<ITypedNeuronAccess, ClusterTypedNeuronAccess>();
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<TypedNeuronTools>();
var app = builder.Build();
app.MapMcp();
app.Run();
