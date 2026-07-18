using System.Reflection;
using Brain.Contracts;
using Brain.Mcp;
using DigitalBrain.AI;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using Xunit;

namespace Brain.Tests.Mcp;

public sealed class McpCorrectionTests
{
    [Fact]
    public void MCP_registers_orleans_client_and_explicit_di()
    {
        var program = File.ReadAllText(Path.Combine(SourceRoot(), "Program.cs"));
        Assert.Contains("UseOrleansClient", program, StringComparison.Ordinal);
        Assert.Contains("ClusterTypedNeuronAccess", program, StringComparison.Ordinal);
        Assert.Contains("JournalOutboxFeedCommandPath", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Salesforce_update_fields_are_typed_dictionary_not_mini_language()
    {
        var method = typeof(TypedNeuronTools).GetMethod(nameof(TypedNeuronTools.SalesforceUpdateRecord));
        Assert.NotNull(method);
        var fields = method!.GetParameters().Single(parameter => parameter.Name == "fields");
        Assert.True(typeof(IReadOnlyDictionary<string, string>).IsAssignableFrom(fields.ParameterType)
            || fields.ParameterType == typeof(Dictionary<string, string>));
        Assert.NotEqual(typeof(string), fields.ParameterType);

        var source = File.ReadAllText(Path.Combine(SourceRoot(), "TypedNeuronTools.cs"));
        Assert.DoesNotContain("Split(';'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("key=value", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task All_six_tools_enter_typed_command_synapse_path()
    {
        var path = new CountingCommandPath();
        var neurons = new CountingNeuronAccess();
        var tools = new TypedNeuronTools(neurons, path);

        await tools.GroupChatStartDiscussion("c1", "t", "g", "k");
        await tools.GroupChatApplyUiAction("c1", "a", 1);
        await tools.GmailListMessages("g1", "q", 5);
        await tools.GmailSendMessage("g1", "to", "s", "b");
        await tools.SalesforceQueryRecords("s1", "SELECT Id FROM Account");
        await tools.SalesforceUpdateRecord("s1", "Account", "001", new Dictionary<string, string> { ["Name"] = "Acme" });

        Assert.Equal(6, path.CreateCount);
        Assert.Equal(6, neurons.DispatchCount);
        Assert.All(path.OrganizationIds, id => Assert.Equal(JournalOutboxFeedCommandPath.OrganizationId, id));
        Assert.All(path.PrincipalIds, id => Assert.Equal(JournalOutboxFeedCommandPath.PrincipalId, id));
        Assert.All(path.SpaceIds, id => Assert.Equal(JournalOutboxFeedCommandPath.SpaceId, id));
    }

    private static string SourceRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Brain.Mcp"));
}

internal sealed class CountingCommandPath : ITypedCommandPath
{
    public int CreateCount { get; private set; }
    public List<OrganizationId> OrganizationIds { get; } = [];
    public List<PrincipalId> PrincipalIds { get; } = [];
    public List<SpaceId> SpaceIds { get; } = [];

    public CommandSynapse<T> CreateCommand<T>(T payload, NeuronAddress source, Guid? commandId = null)
    {
        CreateCount++;
        var command = new JournalOutboxFeedCommandPath().CreateCommand(payload, source, commandId);
        OrganizationIds.Add(command.Metadata.OrganizationId);
        PrincipalIds.Add(command.Metadata.PrincipalId);
        SpaceIds.Add(command.Metadata.SpaceId);
        return command;
    }
}

internal sealed class CountingNeuronAccess : ITypedNeuronAccess
{
    public int DispatchCount { get; private set; }

    public Task<CommandReceipt> StartGroupChatDiscussionAsync(string instanceId, CommandSynapse<StartDiscussion> command)
    {
        DispatchCount++;
        return Accepted(command.Metadata.CommandId);
    }

    public Task<CommandReceipt> ApplyGroupChatUiActionAsync(string instanceId, CommandSynapse<UiActionRequest> command)
    {
        DispatchCount++;
        return Accepted(command.Metadata.CommandId);
    }

    public Task<CommandReceipt> ListGmailMessagesAsync(string instanceId, CommandSynapse<GmailListRequest> command)
    {
        DispatchCount++;
        return Accepted(command.Metadata.CommandId);
    }

    public Task<CommandReceipt> SendGmailMessageAsync(string instanceId, CommandSynapse<GmailSendRequest> command)
    {
        DispatchCount++;
        return Accepted(command.Metadata.CommandId);
    }

    public Task<CommandReceipt> QuerySalesforceRecordsAsync(string instanceId, CommandSynapse<SalesforceQueryRequest> command)
    {
        DispatchCount++;
        return Accepted(command.Metadata.CommandId);
    }

    public Task<CommandReceipt> UpdateSalesforceRecordAsync(string instanceId, CommandSynapse<SalesforceUpdateRequest> command)
    {
        DispatchCount++;
        Assert.Equal("Acme", command.Payload.Fields["Name"]);
        return Accepted(command.Metadata.CommandId);
    }

    private static Task<CommandReceipt> Accepted(Guid commandId) =>
        Task.FromResult(new CommandReceipt(commandId, CommandReceiptStatus.Accepted, 1, null, null));
}
