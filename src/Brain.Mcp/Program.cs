using Brain.Mcp;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ITypedCommandPath, JournalOutboxFeedCommandPath>();
builder.Services.AddSingleton<ITypedNeuronAccess, ClusterTypedNeuronAccess>();
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<TypedNeuronTools>();
var app = builder.Build();
app.MapMcp();
app.Run();
