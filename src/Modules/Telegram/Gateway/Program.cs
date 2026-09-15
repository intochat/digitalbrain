using DigitalBrain.Telegram.Gateway;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = TelegramGateway.MaxBodyBytes);
builder.Services.AddSingleton(_ => new HttpClient(new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    UseCookies = false,
    UseProxy = false,
}) { Timeout = TimeSpan.FromSeconds(45) });
builder.Services.AddSingleton(services => new TelegramGateway(services.GetRequiredService<HttpClient>(),
    builder.Configuration["TelegramGateway:KernelUrl"] ?? throw new InvalidOperationException("The Telegram gateway kernel endpoint is required.")));
var app = builder.Build();
app.Run(context => app.Services.GetRequiredService<TelegramGateway>().HandleAsync(context));
app.Run();
