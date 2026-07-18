using Brain.AgentGateway;
using Microsoft.Agents.AI.DevUI;

var builder = WebApplication.CreateBuilder(args);
builder.AddAgentGateway();
var app = builder.Build();
app.MapDevUI();
app.Run();
