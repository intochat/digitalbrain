using Brain.Client;

var builder = WebApplication.CreateBuilder(args);
builder.AddBrainClient();
builder.Services.AddMcpServer().WithHttpTransport().WithTools<NeuronTools>();
var app = builder.Build();
app.MapMcp();
app.Run();
