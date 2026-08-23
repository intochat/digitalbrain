using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Client;
using DigitalBrain.SmartPrompt;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class SmartPromptTools(IDigitalBrain brain)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    [McpServerTool(Name = McpSurface.ListSmartPrompts)]
    [Description("List known Smart Prompts and their enabled state for the brain owner.")]
    public async Task<string> ListSmartPromptsAsync(CancellationToken cancellationToken = default)
    {
        await brain.ActivateAsync(cancellationToken);

        var prompts = new List<object>();
        foreach (var name in DefaultSmartPromptNames.All)
        {
            var state = await brain.GetEntity<ISmartPrompt>(name).Read();
            prompts.Add(new
            {
                name,
                exists = state is not null,
                title = state?.Document.Title,
                enabled = state?.Document.Enabled,
                bindings = state?.Document.Bindings.Select(binding => new
                {
                    binding.Kind,
                    binding.Label,
                    binding.Account,
                }),
                activeRevisionId = state?.ActiveRevisionId,
            });
        }

        return JsonSerializer.Serialize(new { prompts }, JsonOptions);
    }

    [McpServerTool(Name = McpSurface.SaveSmartPrompt)]
    [Description("Create or update a Smart Prompt document (English body + binding kinds).")]
    public async Task<string> SaveSmartPromptAsync(
        [Description("Stable prompt name, for example 'new-customers'")] string name,
        [Description("Short title")] string title,
        [Description("Plain-English automation body")] string bodyText,
        [Description("Comma-separated binding kinds: gmail,websearch,salesforce,chart,schedule")]
        string bindingKinds = "gmail,websearch,salesforce,chart,schedule",
        [Description("Whether the prompt is enabled")] bool enabled = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyText);

        await brain.ActivateAsync(cancellationToken);

        var bindings = bindingKinds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(kind => new SmartPromptBinding(kind, Label: kind, Account: null))
            .ToArray();

        var document = new SmartPromptDocument(title.Trim(), bodyText.Trim(), bindings, enabled);
        await brain.GetEntity<ISmartPrompt>(name.Trim()).Save(document);
        return JsonSerializer.Serialize(new { saved = name.Trim(), enabled, bindingCount = bindings.Length }, JsonOptions);
    }

    [McpServerTool(Name = McpSurface.RunSmartPrompt)]
    [Description("Trigger a Smart Prompt now (starts an Execution with fake or real façade capabilities).")]
    public async Task<string> RunSmartPromptAsync(
        [Description("Prompt name to run, for example 'new-customers'")] string name,
        [Description("Caller-generated command id (GUID)")] string commandId,
        [Description("Maximum wait in seconds for run-started confirmation")] int timeoutSeconds = 120,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutSeconds, 1);

        if (!Guid.TryParse(commandId, out var commandIdentity) || commandIdentity == Guid.Empty)
        {
            throw new ArgumentException("The command id must be a non-empty GUID.", nameof(commandId));
        }

        await brain.ActivateAsync(cancellationToken);

        var promptName = name.Trim();
        var command = new CommandId(commandIdentity);
        var runnerId = NeuronId.For<ISmartPromptRunner>(brain.Owner, promptName);

        await brain.GetGrainProxy<ISmartPromptRunner>(promptName)
            .HandleAsync(new RunSmartPrompt(command, promptName, OfferChat: null), cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        await foreach (var page in brain.WatchJournalAsync(
            runnerId,
            JournalKind.Outgoing,
            afterSequence: 0,
            timeout.Token))
        {
            foreach (var delivery in page.Delta)
            {
                if (delivery.Synapse is SmartPromptRunStarted started
                    && started.CommandId == command
                    && string.Equals(started.PromptName, promptName, StringComparison.Ordinal))
                {
                    return JsonSerializer.Serialize(new
                    {
                        promptName,
                        executionId = started.ExecutionId.ToString(),
                        commandId = started.CommandId.ToString(),
                        status = "started",
                    }, JsonOptions);
                }
            }
        }

        throw new TimeoutException(
            $"Smart Prompt '{promptName}' did not emit RunStarted for command '{commandId}' "
            + $"within {timeoutSeconds} seconds.");
    }
}

// Public names list for MCP without referencing the internal SmartPrompt assembly catalog.
internal static class DefaultSmartPromptNames
{
    public static IReadOnlyList<string> All { get; } = ["new-customers"];
}
