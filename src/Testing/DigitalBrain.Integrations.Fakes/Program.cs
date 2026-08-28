using DigitalBrain.ServiceDefaults;
using DigitalBrain.Integrations.Fakes;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var provider = builder.Configuration["FakeMcp:Provider"]?.Trim().ToLowerInvariant()
    ?? throw new InvalidOperationException("FakeMcp:Provider must be 'gmail' or 'salesforce'.");
if (provider is not ("gmail" or "salesforce"))
{
    throw new InvalidOperationException($"Unknown fake MCP provider '{provider}'.");
}

var mcp = builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true);
if (provider == "gmail")
{
    mcp.WithTools<GmailFakeTools>();
}
else
{
    builder.Services.AddSingleton<SalesforceFakeStore>();
    mcp.WithTools<SalesforceFakeTools>();
}

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp("/mcp");
app.Run();
