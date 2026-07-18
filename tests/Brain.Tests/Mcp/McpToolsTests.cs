using System.Reflection;
using Brain.Contracts;
using Brain.Mcp;
using DigitalBrain.AI;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using ModelContextProtocol.Server;
using Xunit;

namespace Brain.Tests.Mcp;

public sealed class McpToolsTests
{
    [Fact]
    public void MCP_tools_are_named_and_typed()
    {
        var tools = typeof(TypedNeuronTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(method => (Method: method, Attr: method.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(pair => pair.Attr is not null)
            .ToList();

        Assert.NotEmpty(tools);

        var names = tools.Select(pair => pair.Attr!.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("groupchat_start_discussion", names);
        Assert.Contains("groupchat_apply_ui_action", names);
        Assert.Contains("gmail_list_messages", names);
        Assert.Contains("gmail_send_message", names);
        Assert.Contains("salesforce_query_records", names);
        Assert.Contains("salesforce_update_record", names);

        foreach (var (method, _) in tools)
        {
            Assert.Equal(typeof(Task<CommandReceipt>), method.ReturnType);
            Assert.DoesNotContain("JsonElement", method.ToString(), StringComparison.Ordinal);
        }

        Assert.NotNull(typeof(TypedNeuronTools).GetCustomAttribute<McpServerToolTypeAttribute>());
    }

    [Fact]
    public void MCP_contains_no_generic_neuron_invoke()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Brain.Mcp"));
        Assert.True(Directory.Exists(sourceRoot), sourceRoot);

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs"))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("neuron_describe", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("neuron_read", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("neuron_invoke", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PayloadJson", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GetAssemblies", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AppDomain", source, StringComparison.Ordinal);
        }

        var toolNames = typeof(TypedNeuronTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => name is not null)
            .ToList();
        Assert.DoesNotContain(toolNames, name => name!.Contains("neuron_invoke", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(toolNames, name => name!.Contains("neuron_describe", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(toolNames, name => name!.Contains("neuron_read", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MCP_command_uses_same_journal_outbox_feed_path()
    {
        var path = new RecordingCommandPath();
        var neurons = new RecordingNeuronAccess();
        var tools = new TypedNeuronTools(neurons, path);

        var receipt = await tools.GroupChatStartDiscussion(
            instanceId: "chat-mcp-1",
            topic: "hello",
            gptKey: "gpt-key",
            grokKey: "grok-key");

        Assert.Equal(CommandReceiptStatus.Accepted, receipt.Status);
        Assert.Equal(1, path.CreateCount);
        Assert.NotNull(path.LastSource);
        Assert.Equal(JournalOutboxFeedCommandPath.OrganizationId, path.LastOrganizationId);
        Assert.Equal(JournalOutboxFeedCommandPath.PrincipalId, path.LastPrincipalId);
        Assert.Equal(JournalOutboxFeedCommandPath.SpaceId, path.LastSpaceId);
        Assert.NotNull(neurons.LastStartDiscussion);
        Assert.Equal("hello", neurons.LastStartDiscussion!.Payload.Topic);
        Assert.Equal(path.LastSource, neurons.LastStartDiscussion.Metadata.Source);
        Assert.Equal(JournalOutboxFeedCommandPath.OrganizationId, neurons.LastStartDiscussion.Metadata.OrganizationId);
        Assert.Equal(JournalOutboxFeedCommandPath.PrincipalId, neurons.LastStartDiscussion.Metadata.PrincipalId);
        Assert.Equal(JournalOutboxFeedCommandPath.SpaceId, neurons.LastStartDiscussion.Metadata.SpaceId);
        Assert.Equal("chat-mcp-1", neurons.LastInstanceId);
    }
}

internal sealed class RecordingCommandPath : ITypedCommandPath
{
    public int CreateCount { get; private set; }
    public NeuronAddress? LastSource { get; private set; }
    public OrganizationId? LastOrganizationId { get; private set; }
    public PrincipalId? LastPrincipalId { get; private set; }
    public SpaceId? LastSpaceId { get; private set; }

    public CommandSynapse<T> CreateCommand<T>(T payload, NeuronAddress source, Guid? commandId = null)
    {
        CreateCount++;
        LastSource = source;
        var command = new JournalOutboxFeedCommandPath().CreateCommand(payload, source, commandId);
        LastOrganizationId = command.Metadata.OrganizationId;
        LastPrincipalId = command.Metadata.PrincipalId;
        LastSpaceId = command.Metadata.SpaceId;
        return command;
    }
}

internal sealed class RecordingNeuronAccess : ITypedNeuronAccess
{
    public string? LastInstanceId { get; private set; }
    public CommandSynapse<StartDiscussion>? LastStartDiscussion { get; private set; }

    public Task<CommandReceipt> StartGroupChatDiscussionAsync(string instanceId, CommandSynapse<StartDiscussion> command)
    {
        LastInstanceId = instanceId;
        LastStartDiscussion = command;
        return Task.FromResult(new CommandReceipt(
            command.Metadata.CommandId,
            CommandReceiptStatus.Accepted,
            1,
            null,
            null));
    }

    public Task<CommandReceipt> ApplyGroupChatUiActionAsync(string instanceId, CommandSynapse<UiActionRequest> command) =>
        Task.FromResult(new CommandReceipt(command.Metadata.CommandId, CommandReceiptStatus.Accepted, 1, null, null));

    public Task<CommandReceipt> ListGmailMessagesAsync(string instanceId, CommandSynapse<GmailListRequest> command) =>
        Task.FromResult(new CommandReceipt(command.Metadata.CommandId, CommandReceiptStatus.Accepted, 1, null, null));

    public Task<CommandReceipt> SendGmailMessageAsync(string instanceId, CommandSynapse<GmailSendRequest> command) =>
        Task.FromResult(new CommandReceipt(command.Metadata.CommandId, CommandReceiptStatus.Accepted, 1, null, null));

    public Task<CommandReceipt> QuerySalesforceRecordsAsync(string instanceId, CommandSynapse<SalesforceQueryRequest> command) =>
        Task.FromResult(new CommandReceipt(command.Metadata.CommandId, CommandReceiptStatus.Accepted, 1, null, null));

    public Task<CommandReceipt> UpdateSalesforceRecordAsync(string instanceId, CommandSynapse<SalesforceUpdateRequest> command) =>
        Task.FromResult(new CommandReceipt(command.Metadata.CommandId, CommandReceiptStatus.Accepted, 1, null, null));
}
