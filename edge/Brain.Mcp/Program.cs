using Brain.Client;
using DigitalBrain.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddBrainClient();
builder.Services.AddMcpServer().WithHttpTransport().WithTools<NeuronTools>();
var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp();
app.Run();
