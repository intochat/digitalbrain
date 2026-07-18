using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AI.Contracts;
using Brain.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Brain.Modules.Ai;

public sealed class LlmKind(ModelCatalog catalog, IServiceProvider services) : INeuronKind
{
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(60);
    private const int MaximumPromptBytes = 32768;
    private const int MaximumOutputTokens = 4096;

    public string Kind => "llm";
    public string[] Contracts => ["llm.complete.v1", AiCapabilityIds.TextGenerate];

    public ValueTask<KindResult> InvokeAsync(NeuronContext context, NeuronInvocation invocation) =>
        invocation.Contract switch
        {
            "llm.complete.v1" => HandleCompleteAsync(context, invocation.InputJson),
            AiCapabilityIds.TextGenerate => HandleGenerateAsync(context, invocation.InputJson),
            _ => throw new BrainException(BrainErrors.UnknownContract, invocation.Contract)
        };

    public string Project(NeuronContext context, string projection)
    {
        var completions = 0;
        string? lastModel = null;

        foreach (var evt in context.Journal)
        {
            if (evt.Kind != "llm.completed")
                continue;

            completions++;
            using var doc = JsonDocument.Parse(evt.PayloadJson);
            if (doc.RootElement.TryGetProperty("model", out var modelElement))
                lastModel = modelElement.GetString();
        }

        return JsonSerializer.Serialize(new { completions, model = lastModel });
    }

    private async ValueTask<KindResult> HandleCompleteAsync(NeuronContext context, string inputJson)
    {
        var (prompt, requestedMaxOutputTokens) = ParseRequest(inputJson);
        var maxOutputTokens = Math.Clamp(requestedMaxOutputTokens ?? 1024, 1, MaximumOutputTokens);

        return await GenerateAsync(
            context,
            [new ChatMessage(ChatRole.User, prompt)],
            prompt,
            maxOutputTokens,
            includeModelMetadata: true);
    }

    private async ValueTask<KindResult> HandleGenerateAsync(NeuronContext context, string inputJson)
    {
        var request = ParseGenerationRequest(inputJson);
        var prompt = $"{request.Instruction}\n\n{request.Input}";

        return await GenerateAsync(
            context,
            [
                new ChatMessage(ChatRole.System, request.Instruction),
                new ChatMessage(ChatRole.User, request.Input)
            ],
            prompt,
            request.MaximumOutputTokens,
            includeModelMetadata: false);
    }

    private async ValueTask<KindResult> GenerateAsync(
        NeuronContext context,
        IEnumerable<ChatMessage> messages,
        string prompt,
        int maxOutputTokens,
        bool includeModelMetadata)
    {

        var tier = ModelCatalog.ParseTier(context.Address.NeuronId);
        var binding = catalog.Resolve(tier);
        var client = services.GetKeyedService<IChatClient>(binding.Provider)
            ?? throw new BrainException(BrainErrors.ModelUnavailable, $"no chat client registered for provider '{binding.Provider}'");

        using var deadline = new CancellationTokenSource(CompletionTimeout);
        ChatResponse response;
        try
        {
            response = await client.GetResponseAsync(
                messages,
                new ChatOptions { MaxOutputTokens = maxOutputTokens, ModelId = binding.Model },
                deadline.Token);
        }
        catch (OperationCanceledException)
        {
            throw new BrainException(BrainErrors.ModelTimeout, $"model '{binding.Model}' timed out after {CompletionTimeout.TotalSeconds}s");
        }

        var text = response.Text;
        var eventPayload = JsonSerializer.Serialize(new
        {
            promptSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prompt))),
            response = TruncateUtf8(text, 8192),
            model = binding.Model,
            tier = tier.ToString().ToLowerInvariant()
        });
        var output = includeModelMetadata
            ? JsonSerializer.Serialize(new { text, model = binding.Model, revision = context.Revision + 1 })
            : JsonSerializer.Serialize(new TextGenerationResult(text));

        return new KindResult(output, [("llm.completed", eventPayload)]);
    }

    private static TextGenerationRequest ParseGenerationRequest(string inputJson)
    {
        TextGenerationRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<TextGenerationRequest>(
                inputJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            throw new BrainException("input.invalid", "malformed json");
        }

        if (request is null)
            throw new BrainException("input.invalid", "request is required");
        if (string.IsNullOrWhiteSpace(request.Instruction))
            throw new BrainException("input.invalid", "instruction cannot be empty");
        if (string.IsNullOrWhiteSpace(request.Input))
            throw new BrainException("input.invalid", "input cannot be empty");
        if (request.MaximumOutputTokens is < 1 or > MaximumOutputTokens)
            throw new BrainException("input.invalid", $"maximumOutputTokens must be between 1 and {MaximumOutputTokens}");

        var promptBytes = Encoding.UTF8.GetByteCount(request.Instruction)
            + Encoding.UTF8.GetByteCount(request.Input);
        if (promptBytes > MaximumPromptBytes)
            throw new BrainException("input.invalid", $"instruction and input exceed maximum size of {MaximumPromptBytes} bytes");

        return request;
    }

    private static (string Prompt, int? MaxOutputTokens) ParseRequest(string inputJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(inputJson);
        }
        catch (JsonException)
        {
            throw new BrainException("input.invalid", "malformed json");
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (!root.TryGetProperty("prompt", out var promptElement) || promptElement.ValueKind != JsonValueKind.String)
                throw new BrainException("input.invalid", "prompt field is required");

            var prompt = promptElement.GetString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(prompt))
                throw new BrainException("input.invalid", "prompt cannot be empty");

            if (Encoding.UTF8.GetByteCount(prompt) > MaximumPromptBytes)
                throw new BrainException("input.invalid", $"prompt exceeds maximum size of {MaximumPromptBytes} bytes");

            var maxOutputTokens = root.TryGetProperty("maxOutputTokens", out var tokensElement) && tokensElement.ValueKind == JsonValueKind.Number
                ? tokensElement.GetInt32()
                : (int?)null;

            return (prompt, maxOutputTokens);
        }
    }

    private static string TruncateUtf8(string value, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= maxBytes)
            return value;

        var length = maxBytes;
        while (length > 0 && (bytes[length] & 0xC0) == 0x80)
            length--;
        return Encoding.UTF8.GetString(bytes, 0, length);
    }
}
