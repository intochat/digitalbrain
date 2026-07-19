using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException(
        "The DigitalBrain quickstart controlled provider is disabled outside Development.");
var expectedOpenAIKey =
    builder.Configuration["DigitalBrain:Quickstart:OpenAISecret"];
var expectedAnthropicKey =
    builder.Configuration["DigitalBrain:Quickstart:AnthropicSecret"];
if (string.IsNullOrWhiteSpace(expectedOpenAIKey) ||
    string.IsNullOrWhiteSpace(expectedAnthropicKey))
    throw new InvalidOperationException(
        "The controlled provider requires explicit synthetic credentials.");
var app = builder.Build();
var requests = new ConcurrentQueue<ControlledProviderRequest>();
var sequence = 0;

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/requests", () => Results.Ok(requests.ToArray()));

app.MapPost("/v1/chat/completions", async (HttpRequest request) =>
{
    using var document = await JsonDocument.ParseAsync(request.Body);
    var model = RequiredString(document.RootElement, "model");
    var input = LatestMessage(document.RootElement);
    var authorized = string.Equals(
        request.Headers.Authorization.ToString(),
        $"Bearer {expectedOpenAIKey}",
        StringComparison.Ordinal);
    if (!authorized)
        return Results.Unauthorized();

    var current = Interlocked.Increment(ref sequence);
    requests.Enqueue(new ControlledProviderRequest(
        current,
        "openai",
        "chat",
        model,
        InputHash(input),
        authorized));
    var text = ControlledText("openai", model, current, input);
    return Results.Json(new
    {
        id = $"chatcmpl-controlled-{current}",
        @object = "chat.completion",
        created = 1,
        model,
        choices = new[]
        {
            new
            {
                index = 0,
                message = new { role = "assistant", content = text },
                finish_reason = "stop"
            }
        },
        usage = new
        {
            prompt_tokens = 1,
            completion_tokens = 1,
            total_tokens = 2
        }
    });
});

app.MapPost("/v1/embeddings", async (HttpRequest request) =>
{
    using var document = await JsonDocument.ParseAsync(request.Body);
    var model = RequiredString(document.RootElement, "model");
    var input = document.RootElement
        .GetProperty("input")[0]
        .GetString() ?? string.Empty;
    var authorized = string.Equals(
        request.Headers.Authorization.ToString(),
        $"Bearer {expectedOpenAIKey}",
        StringComparison.Ordinal);
    if (!authorized)
        return Results.Unauthorized();

    var current = Interlocked.Increment(ref sequence);
    requests.Enqueue(new ControlledProviderRequest(
        current,
        "openai",
        "embedding",
        model,
        InputHash(input),
        authorized));
    return Results.Json(new
    {
        @object = "list",
        data = new[]
        {
            new
            {
                @object = "embedding",
                index = 0,
                embedding = new[] { 0.25f, -0.5f, 0.75f }
            }
        },
        model,
        usage = new { prompt_tokens = 1, total_tokens = 1 }
    });
});

app.MapPost("/v1/messages", async (HttpRequest request) =>
{
    using var document = await JsonDocument.ParseAsync(request.Body);
    var model = RequiredString(document.RootElement, "model");
    var input = LatestMessage(document.RootElement);
    var authorized = string.Equals(
            request.Headers["x-api-key"].ToString(),
            expectedAnthropicKey,
            StringComparison.Ordinal) &&
        request.Headers["anthropic-version"].ToString() == "2023-06-01";
    if (!authorized)
        return Results.Unauthorized();

    var current = Interlocked.Increment(ref sequence);
    requests.Enqueue(new ControlledProviderRequest(
        current,
        "anthropic",
        "chat",
        model,
        InputHash(input),
        authorized));
    var text = ControlledText("anthropic", model, current, input);
    return Results.Json(new
    {
        id = $"msg_controlled_{current}",
        type = "message",
        role = "assistant",
        model,
        content = new[] { new { type = "text", text } },
        stop_reason = "end_turn",
        stop_sequence = (string?)null,
        usage = new { input_tokens = 1, output_tokens = 1 }
    });
});

await app.RunAsync();

static string RequiredString(JsonElement element, string propertyName) =>
    element.GetProperty(propertyName).GetString() ??
    throw new InvalidOperationException($"{propertyName} is required.");

static string LatestMessage(JsonElement root)
{
    var messages = root.GetProperty("messages");
    if (messages.GetArrayLength() == 0)
        throw new InvalidOperationException("A provider message is required.");
    var content = messages[messages.GetArrayLength() - 1].GetProperty("content");
    if (content.ValueKind == JsonValueKind.String)
        return content.GetString() ?? string.Empty;
    foreach (var item in content.EnumerateArray())
    {
        if (item.TryGetProperty("text", out var text) &&
            text.ValueKind == JsonValueKind.String)
            return text.GetString() ?? string.Empty;
    }
    throw new InvalidOperationException("Provider message text is required.");
}

static string ControlledText(
    string provider,
    string model,
    int sequence,
    string input) =>
    $"controlled:{provider}:{model}:{sequence}:{input}";

static string InputHash(string input) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))
        .ToLowerInvariant();

internal sealed record ControlledProviderRequest(
    int Sequence,
    string Provider,
    string Capability,
    string Model,
    string InputHash,
    bool Authorized);
