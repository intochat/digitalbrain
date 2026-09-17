using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions.Programming;
using DigitalBrain.Core.Programming;
using Microsoft.Extensions.AI;

namespace IntoChat;

public sealed record ProgramCompilation(ProgramDefinition Definition, ProgramValidation Validation);

public sealed class ProgramCompiler(IChatClient client, ProgramService programs)
{
    private const int MaximumIntentBytes = 16 * 1024;
    private const int MaximumSourceBytes = 32 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ProgramCompilation> CompileAsync(string intent, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intent);
        if (Encoding.UTF8.GetByteCount(intent) > MaximumIntentBytes)
        {
            throw new ArgumentException("Describe a behavior in at most 16 KB of text.", nameof(intent));
        }

        List<ChatMessage> messages =
        [
            new(ChatRole.System, Instructions),
            new(ChatRole.User, intent),
        ];
        string? failure = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(60));
            ChatResponse response;
            try
            {
                response = await client.GetResponseAsync(messages, new ChatOptions
                {
                    Tools = [],
                    ResponseFormat = ChatResponseFormat.Json,
                    MaxOutputTokens = 8_000,
                    Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Low },
                }, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The AI compiler did not finish within 60 seconds. Your live programs are unchanged.", error);
            }

            var text = response.Text;
            try
            {
                if (Encoding.UTF8.GetByteCount(text) > ProgramValidator.MaxDefinitionBytes)
                {
                    throw new InvalidOperationException("The generated graph exceeds the 64 KB limit.");
                }
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.TryGetProperty("error", out var unsupported))
                {
                    throw new NotSupportedException(unsupported.GetString() ?? "The requested behavior cannot be expressed by the available neurons.");
                }
                var definition = document.RootElement.Deserialize<ProgramDefinition>(Json)
                    ?? throw new JsonException("The compiler returned an empty graph.");
                var validation = Validate(definition);
                if (validation.Valid || attempt == 1)
                {
                    return new(definition, validation);
                }
                failure = string.Join("\n", validation.Errors);
            }
            catch (JsonException error)
            {
                failure = "Return a complete ProgramDefinition JSON object: " + error.Message;
            }
            catch (InvalidOperationException error)
            {
                failure = error.Message;
            }

            if (attempt == 0)
            {
                if (Encoding.UTF8.GetByteCount(text) <= ProgramValidator.MaxDefinitionBytes)
                {
                    messages.Add(new(ChatRole.Assistant, text));
                }
                messages.Add(new(ChatRole.User, "Correct the draft to satisfy these validation errors. Return only the complete JSON graph: " + failure));
            }
        }

        throw new InvalidOperationException("The AI compiler could not produce a valid graph: " + failure);
    }

    private ProgramValidation Validate(ProgramDefinition definition)
    {
        var validation = programs.Validate(definition);
        var errors = validation.Errors.ToList();
        foreach (var node in definition.Nodes ?? [])
        {
            if (node is null || node.Config.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            if (node.Kind == "code"
                && (!node.Config.TryGetProperty("source", out var source)
                    || source.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(source.GetString())
                    || Encoding.UTF8.GetByteCount(source.GetString()!) > MaximumSourceBytes))
            {
                errors.Add($"Code node '{node.Id}' requires nonempty C# source of at most 32 KB.");
            }
        }
        return new(errors.Count == 0, errors, validation.Order);
    }

    private const string Instructions = """
        Compile the user's intent into a DigitalBrain neuron graph draft. Never deploy, execute code,
        call a tool, or claim it is running. Return only one JSON object, no markdown.
        Shape: {"id":"short-lowercase-name","name":"Readable name","trigger":"SignalName",
        "description":"What it does; include a useful JSON example input",
        "nodes":[{"id":"input","kind":"input","config":{},"inputType":"Json","outputType":"Json"},
        {"id":"output","kind":"output","config":{},"inputType":"Json","outputType":"Json"}],
        "synapses":[{"from":"input","to":"output"}]}.
        All nodes need id, kind, config, inputType, outputType. At most 64 nodes, 256 edges, 64 KB total.
        Use lowercase kinds. Program ids have at most 48 letters/digits/underscore/hyphen; node ids
        have at most 64 characters and start with a letter.
        Trigger is letters only, 1-64 characters. All roots must be input nodes; no edges into input;
        every other node must be connected. Graphs are acyclic. Use inputType/outputType Json unless
        explicit semantic types help. Each edge must connect equal types or have Json at one end.
        End with output node(s). A join receives a JSON object keyed by upstream node ids.

        Expressions: {{input.name}} reads original input, {{value.name}} reads current upstream value,
        {{nodes.nodeId.field}} reads a completed predecessor. Paths support dot and [0] indexing.
        A string containing exactly one expression preserves its JSON type; surrounding text creates
        a string. Missing fields become null. There is no arithmetic/expression evaluator in templates.

        Available kinds and config:
        input: {} emits original input.
        template: {"template":any JSON with expressions}; transforms the incoming value.
        output: {} passes through, or {"template":any JSON with expressions} projects final output.
        filter: {"path":"input.score","operator":"gte","value":70}; passes the whole current value
        when true, otherwise stops that branch. Operators eq, ne, gt, gte, lt, lte, contains, startswith,
        endswith, exists, in. exists needs no value. contains/startswith/endswith ignore case for text.
        Compound predicates: {"all":[predicate,...]}, {"any":[predicate,...]}, {"not":predicate}.
        filter does NOT filter individual array items. Use code for an array filter.
        map: {"path":"input.contacts","template":{"name":"{{value.name}}"}} maps each array item;
        on a non-array it projects one value. Default path selects current value.
        aggregate: {"path":"input.items","operation":"sum","field":"amount"}; operation count,
        sum, avg, min, max. Requires an array, field optional for arrays of numbers. Returns a number.
        call: {"neuron":"timer:example","interface":"timer","method":"read","arguments":{}}.
        This is a typed neuron call, not arbitrary HTTP. The timer read alias above is known. Never
        invent other aliases: only use exact method contracts the user supplies. Arguments may use
        expressions. It must be obvious in description if a call has side effects.
        agent: {"instructions":"Task for the AI","prompt":"optional text using {{value}}"}; an AI
        transformation returning {"text":"the AI response"}. Do not use an agent for simple
        deterministic transforms. For the existing IntoChat chat use {"mode":"conversation",
        "instructions":"Optional changes to its assistant behavior"}; preserve its conversation input.
        Only when the user explicitly asks to change IntoChat's conversation, use id "intochat",
        name "IntoChat conversation", trigger "ChatMessage", and input->assistant->output, with the
        requested behavior in assistant's instructions. Otherwise never use the reserved id "intochat".
        code: {"source":"full single-file C# source"}. Trusted local C#, not a security sandbox.
        Read one JSON value from Console.In.ReadToEndAsync(), parse with System.Text.Json, write one
        JSON value with Console.WriteLine(JsonSerializer.Serialize(result)); diagnostics to Console.Error.
        Full C# source is at most 32 KB. Execution has 30 seconds including build and 64 KB output.
        No package dependencies unless explicitly requested. Use code only when needed or requested.
        Example code: using System.Text.Json; using var d = JsonDocument.Parse(await Console.In.ReadToEndAsync());
        Console.WriteLine(JsonSerializer.Serialize(new { doubled = d.RootElement.GetProperty("value").GetDecimal() * 2 }));
        Every code draft description must state "Trusted local C#" and explain what it executes.

        Examples: greeting input->template {"template":{"message":"Hello, {{input.name}}!"}}->output.
        Lead qualification input->filter score gte 70->output. Order summary branches from input to
        count and sum aggregate nodes, then connects both to output with template
        {"count":"{{nodes.count}}","total":"{{nodes.total}}"}.
        A trigger is a signal fired by a caller; merely naming it does not install a scheduler or
        external webhook. Never claim automatic polling or an external integration was configured.
        If the intent cannot be expressed with these capabilities, return {"error":"precise reason"}.
        """;
}
