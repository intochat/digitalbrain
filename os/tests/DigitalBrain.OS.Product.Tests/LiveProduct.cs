using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace DigitalBrain.ProductTests;

public sealed class LiveProduct
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);
    private const string AppHostPath = "os/DigitalBrain.OS.AppHost/DigitalBrain.OS.AppHost.csproj";
    private const string McpResource = "digitalbrain-mcp";

    [Fact(
        Explicit = true,
        Timeout = 900_000,
        DisplayName =
            "LIVE product: real Gemma chat is idempotent, journaled, owner-scoped and emits content-rich GenAI telemetry in Development")]
    public async Task RealChatIsObservable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = FindRepositoryRoot();
        var chatName = $"product-verification-{Guid.NewGuid():N}";
        var commandId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        const string prompt = "Tell me your name in one short sentence.";
        var started = false;

        await RunAspireAsync(
            repository,
            allowFailure: true,
            CancellationToken.None,
            "stop",
            "--apphost",
            AppHostPath,
            "--non-interactive",
            "--nologo");

        try
        {
            await RunAspireAsync(
                repository,
                allowFailure: false,
                cancellationToken,
                "start",
                "--apphost",
                AppHostPath,
                "--format",
                "Json",
                "--non-interactive",
                "--nologo");
            started = true;

            foreach (var resource in new[] { "silo", McpResource, "brain-ai-gemma4" })
            {
                await RunAspireAsync(
                    repository,
                    allowFailure: false,
                    cancellationToken,
                    "wait",
                    resource,
                    "--apphost",
                    AppHostPath,
                    "--timeout",
                    "300",
                    "--non-interactive",
                    "--nologo");
            }

            var sendInput = JsonSerializer.Serialize(
                new
                {
                    text = prompt,
                    commandId,
                    chatName,
                    timeoutSeconds = 300,
                });
            var result = await CallToolAsync(
                repository,
                "send_chat_message",
                sendInput,
                cancellationToken);
            var response = RequiredString(result, "response");
            var correlationId = RequiredString(result, "correlationId");

            Assert.False(string.IsNullOrWhiteSpace(response));
            Assert.True(Guid.TryParse(RequiredString(result, "commandId"), out _));
            Assert.True(Guid.TryParse(correlationId, out _));

            var retried = await CallToolAsync(
                repository,
                "send_chat_message",
                sendInput,
                cancellationToken);
            Assert.Equal(response, RequiredString(retried, "response"));
            Assert.Equal(correlationId, RequiredString(retried, "correlationId"));

            var transcript = await CallToolAsync(
                repository,
                "read_chat_transcript",
                JsonSerializer.Serialize(new { chatName }),
                cancellationToken);
            var turns = RequiredArray(transcript, "turns");
            Assert.Collection(
                turns,
                turn =>
                {
                    Assert.Equal("you", RequiredString(turn, "speaker"));
                    Assert.Equal(prompt, RequiredString(turn, "text"));
                },
                turn =>
                {
                    Assert.Equal("brain", RequiredString(turn, "speaker"));
                    Assert.Equal(response, RequiredString(turn, "text"));
                });

            var journal = await CallToolAsync(
                repository,
                "read_neuron_journal",
                JsonSerializer.Serialize(
                    new
                    {
                        grainType = "chat",
                        name = chatName,
                        kind = "outgoing",
                        afterSequence = 0,
                    }),
                cancellationToken);
            var entries = RequiredArray(journal, "entries");
            Assert.Collection(
                entries,
                entry => Assert.Equal("UserMessaged", RequiredString(entry, "synapse")),
                entry =>
                {
                    Assert.Equal("AssistantResponded", RequiredString(entry, "synapse"));
                    Assert.Equal(correlationId, RequiredString(entry, "correlation"));
                });

            var activeNeurons = await CallToolAsync(
                repository,
                "list_active_neurons",
                "{}",
                cancellationToken);
            var activeChat = activeNeurons
                .AsArray()
                .Single(neuron =>
                    string.Equals(RequiredString(neuron, "grainType"), "chat", StringComparison.Ordinal)
                    && string.Equals(
                        RequiredString(neuron, "identity"),
                        $"dev/{chatName}",
                        StringComparison.Ordinal));
            Assert.NotNull(activeChat);
            Assert.DoesNotContain(
                "\"silo\"",
                activeNeurons.ToJsonString(),
                StringComparison.OrdinalIgnoreCase);

            var span = await WaitForGenAiSpanAsync(repository, prompt, cancellationToken);
            var attributes = RequiredObject(span, "attributes");

            Assert.Equal("chat", RequiredString(attributes, "gen_ai.operation.name"));
            Assert.Equal("ollama", RequiredString(attributes, "gen_ai.provider.name"));
            Assert.Contains(
                "gemma4",
                RequiredString(attributes, "gen_ai.request.model"),
                StringComparison.OrdinalIgnoreCase);
            Assert.True(RequiredLong(attributes, "gen_ai.usage.input_tokens") > 0);
            Assert.True(RequiredLong(attributes, "gen_ai.usage.output_tokens") > 0);
            Assert.False(
                string.IsNullOrWhiteSpace(
                    RequiredString(attributes, "gen_ai.response.finish_reasons")));
            Assert.Contains(
                prompt,
                RequiredString(attributes, "gen_ai.input.messages"),
                StringComparison.Ordinal);
            Assert.Contains(
                response,
                RequiredString(attributes, "gen_ai.output.messages"),
                StringComparison.Ordinal);
        }
        finally
        {
            if (started)
            {
                await RunAspireAsync(
                    repository,
                    allowFailure: true,
                    CancellationToken.None,
                    "stop",
                    "--apphost",
                    AppHostPath,
                    "--non-interactive",
                    "--nologo");
            }
        }
    }

    private static async Task<JsonNode> CallToolAsync(
        string repository,
        string tool,
        string input,
        CancellationToken cancellationToken)
    {
        var result = await RunAspireAsync(
            repository,
            allowFailure: false,
            cancellationToken,
            "mcp",
            "call",
            McpResource,
            tool,
            "--input",
            input,
            "--apphost",
            AppHostPath,
            "--non-interactive",
            "--nologo");
        return ParseJson(result.StandardOutput);
    }

    private static async Task<JsonObject> WaitForGenAiSpanAsync(
        string repository,
        string prompt,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var result = await RunAspireAsync(
                repository,
                allowFailure: false,
                cancellationToken,
                "otel",
                "spans",
                "--apphost",
                AppHostPath,
                "--format",
                "Json",
                "--limit",
                "100",
                "--search",
                "gen_ai",
                "--non-interactive",
                "--nologo");
            var spans = ParseJson(result.StandardOutput).AsArray();
            var span = spans
                .OfType<JsonObject>()
                .FirstOrDefault(candidate =>
                {
                    var attributes = candidate["attributes"] as JsonObject;
                    return attributes is not null
                        && string.Equals(
                            OptionalString(attributes, "gen_ai.operation.name"),
                            "chat",
                            StringComparison.Ordinal)
                        && string.Equals(
                            OptionalString(attributes, "gen_ai.provider.name"),
                            "ollama",
                            StringComparison.Ordinal)
                        && OptionalString(attributes, "gen_ai.input.messages")
                            ?.Contains(prompt, StringComparison.Ordinal) is true;
                });

            if (span is not null)
            {
                return span;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new Xunit.Sdk.XunitException(
            "No content-rich Ollama GenAI chat span arrived within one minute.");
    }

    private static async Task<CommandResult> RunAspireAsync(
        string repository,
        bool allowFailure,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        using var commandTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        commandTimeout.CancelAfter(CommandTimeout);

        var start = new ProcessStartInfo("aspire")
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            throw new Xunit.Sdk.XunitException("The Aspire CLI process did not start.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(commandTimeout.Token);
        var standardError = process.StandardError.ReadToEndAsync(commandTimeout.Token);

        try
        {
            await process.WaitForExitAsync(commandTimeout.Token);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        var result = new CommandResult(
            process.ExitCode,
            await standardOutput,
            await standardError);

        if (!allowFailure && result.ExitCode != 0)
        {
            throw new Xunit.Sdk.XunitException(
                $"aspire {string.Join(' ', arguments)} exited with {result.ExitCode}.{Environment.NewLine}"
                + result.StandardOutput
                + Environment.NewLine
                + result.StandardError);
        }

        return result;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new Xunit.Sdk.XunitException(
                $"Could not find DigitalBrain.slnx above {AppContext.BaseDirectory}.");
    }

    private static JsonNode ParseJson(string output)
    {
        var lines = output.Split(
            ["\r\n", "\n"],
            StringSplitOptions.None);
        var firstJsonLine = Array.FindIndex(
            lines,
            line =>
            {
                var trimmed = line.TrimStart();
                return trimmed.StartsWith('{', StringComparison.Ordinal)
                    || trimmed.StartsWith('[', StringComparison.Ordinal);
            });

        if (firstJsonLine < 0)
        {
            throw new Xunit.Sdk.XunitException(
                $"The Aspire CLI did not return JSON.{Environment.NewLine}{output}");
        }

        return JsonNode.Parse(string.Join(Environment.NewLine, lines[firstJsonLine..]))
            ?? throw new Xunit.Sdk.XunitException("The Aspire CLI returned JSON null.");
    }

    private static JsonArray RequiredArray(JsonNode? node, string property)
        => node?[property] as JsonArray
            ?? throw new Xunit.Sdk.XunitException(
                $"Expected JSON array '{property}' in {node?.ToJsonString()}.");

    private static JsonObject RequiredObject(JsonNode? node, string property)
        => node?[property] as JsonObject
            ?? throw new Xunit.Sdk.XunitException(
                $"Expected JSON object '{property}' in {node?.ToJsonString()}.");

    private static string RequiredString(JsonNode? node, string property)
        => OptionalString(node, property)
            ?? throw new Xunit.Sdk.XunitException(
                $"Expected JSON string '{property}' in {node?.ToJsonString()}.");

    private static string? OptionalString(JsonNode? node, string property)
    {
        var value = node?[property];
        if (value is null)
        {
            return null;
        }

        return value is JsonValue jsonValue
               && jsonValue.TryGetValue<string>(out var text)
            ? text
            : value.ToJsonString();
    }

    private static long RequiredLong(JsonNode? node, string property)
    {
        var value = node?[property];
        if (value is JsonValue jsonValue
            && jsonValue.TryGetValue<long>(out var number))
        {
            return number;
        }

        if (long.TryParse(OptionalString(node, property), CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw new Xunit.Sdk.XunitException(
            $"Expected JSON integer '{property}' in {node?.ToJsonString()}.");
    }

    private sealed record CommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
