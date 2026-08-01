using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Salesforce;
using DigitalBrain.Testing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace DigitalBrain.Salesforce.Tests;

public sealed class SalesforceIntent(SalesforceFixture fixture)
{
    [Fact(DisplayName = "ISalesforce is a marker INeuron with no declared operation members")]
    public void Marker_is_INeuron_with_no_declared_members()
    {
        Assert.True(typeof(INeuron).IsAssignableFrom(typeof(ISalesforce)));
        Assert.Empty(typeof(ISalesforce).GetMethods().Where(static method => method.DeclaringType == typeof(ISalesforce)));
        Assert.Empty(typeof(ISalesforce).GetProperties().Where(static property => property.DeclaringType == typeof(ISalesforce)));
    }

    [Fact(DisplayName = "SalesforceRequest propose returns AwaitingApproval without opening MCP")]
    public async Task Propose_returns_awaiting_approval_without_mcp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var commandId = CommandId.New();

        var response = await ProposeAsync(test, commandId, SalesforceFixture.SampleDescription, cancellationToken);

        Assert.True(response.Succeeded);
        Assert.NotNull(response.Mutation);
        var mutation = response.Mutation;
        Assert.Equal(commandId, mutation.CommandId);
        Assert.Equal(SalesforceFixture.SampleAccountId, mutation.AccountId);
        Assert.Equal(SalesforceFixture.SampleDescription, mutation.Description);
        Assert.Equal(SalesforceMutationState.AwaitingApproval, mutation.State);
        Assert.Equal(0, test.Mcp().SessionCount);

        var again = await ProposeAsync(test, commandId, SalesforceFixture.SampleDescription, cancellationToken);
        Assert.Equal(mutation, again.Mutation);
        Assert.Equal(0, test.Mcp().SessionCount);
    }

    [Fact(DisplayName = "SalesforceRequest propose rejects CommandId reuse with different content")]
    public async Task Propose_rejects_command_reuse_with_different_content()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var commandId = CommandId.New();
        var first = await ProposeAsync(test, commandId, SalesforceFixture.SampleDescription, cancellationToken);

        var failure = await ProposeAsync(
            test,
            commandId,
            SalesforceFixture.SampleDescription + "\n(amended)",
            cancellationToken);

        Assert.False(failure.Succeeded);
        Assert.Contains("fingerprint", failure.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            first.Mutation,
            (await ProposeAsync(test, commandId, SalesforceFixture.SampleDescription, cancellationToken)).Mutation);
    }

    [Fact(DisplayName = "ApproveSalesforceMutation completes after admitted MCP update")]
    public async Task Approve_completes_through_scripted_mcp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var commandId = CommandId.New();
        var proposed = await ProposeAsync(test, commandId, SalesforceFixture.SampleDescription, cancellationToken);
        Assert.True(proposed.Succeeded);

        CatalogWrite(test, SalesforceFixture.SampleDescription);
        var approval = new SalesforceMutationApproval(
            Guid.NewGuid(),
            commandId,
            proposed.Mutation!.Fingerprint,
            ISessionNeuron.ForOwner(test.Client.Owner),
            test.Clock.UtcNow);

        var approved = await test.Client.Get<ISalesforce>(SalesforceFixture.Connection)
            .SendAsync(new ApproveSalesforceMutation(approval), cancellationToken);

        Assert.True(approved.Succeeded);
        Assert.Equal(SalesforceMutationState.Completed, approved.Mutation!.State);
        Assert.True(test.Mcp().SessionCount >= 1);

        var again = await test.Client.Get<ISalesforce>(SalesforceFixture.Connection)
            .SendAsync(new ApproveSalesforceMutation(approval), cancellationToken);
        Assert.Equal(approved.Mutation, again.Mutation);
    }

    [Fact(DisplayName = "ApproveSalesforceMutation rejects fingerprint that does not match the proposal")]
    public async Task Approve_rejects_mismatched_fingerprint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var commandId = CommandId.New();
        var proposed = await ProposeAsync(test, commandId, SalesforceFixture.SampleDescription, cancellationToken);

        var approval = new SalesforceMutationApproval(
            Guid.NewGuid(),
            commandId,
            proposed.Mutation!.Fingerprint + "-tampered",
            ISessionNeuron.ForOwner(test.Client.Owner),
            test.Clock.UtcNow);

        var response = await test.Client.Get<ISalesforce>(SalesforceFixture.Connection)
            .SendAsync(new ApproveSalesforceMutation(approval), cancellationToken);

        Assert.False(response.Succeeded);
        Assert.Contains("fingerprint", response.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, test.Mcp().SessionCount);
    }

    [Fact(DisplayName = "Unapproved mutation cannot complete without ApproveSalesforceMutation")]
    public async Task Unapproved_mutation_stays_awaiting_approval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var commandId = CommandId.New();
        CatalogWrite(test, SalesforceFixture.SampleDescription);

        var proposed = await ProposeAsync(test, commandId, SalesforceFixture.SampleDescription, cancellationToken);
        Assert.Equal(SalesforceMutationState.AwaitingApproval, proposed.Mutation!.State);
        Assert.Equal(0, test.Mcp().SessionCount);
    }

    private static Task<SalesforceResponse> ProposeAsync(
        TestBrain test,
        CommandId commandId,
        string description,
        CancellationToken cancellationToken)
        => test.Client.Get<ISalesforce>(SalesforceFixture.Connection)
            .SendAsync(
                new SalesforceRequest(
                    $"Propose Account Description for {SalesforceFixture.SampleAccountId}",
                    commandId,
                    SalesforceFixture.SampleAccountId,
                    description),
                cancellationToken);

    private static void CatalogWrite(TestBrain test, string description)
        => test.Mcp().Catalog(
            SalesforceFixture.ServerKey,
            SalesforceTools.UpdateAccount(),
            SalesforceTools.SoqlQuery(SalesforceFixture.SampleAccountId, description));
}

internal static class SalesforceTools
{
    private const string Type = "type";
    private const string Properties = "properties";
    private const string Required = "required";
    private const string Items = "items";
    private const string String = "string";
    private const string Object = "object";
    private const string Boolean = "boolean";
    private const string Array = "array";

    private static readonly Dictionary<string, object?> StringProp = new() { [Type] = String };
    private static readonly Dictionary<string, object?> ObjectProp = new() { [Type] = Object };
    private static readonly Dictionary<string, object?> BooleanProp = new() { [Type] = Boolean };
    private static readonly Dictionary<string, object?> ArrayOfObjects = new()
    {
        [Type] = Array,
        [Items] = ObjectProp,
    };

    private static readonly ToolAnnotations ReadOnlyAdmitted = new()
    {
        ReadOnlyHint = true,
        DestructiveHint = false,
        IdempotentHint = true,
        OpenWorldHint = false,
    };

    private static readonly ToolAnnotations DestructiveAdmitted = new()
    {
        ReadOnlyHint = false,
        DestructiveHint = true,
        IdempotentHint = false,
        OpenWorldHint = false,
    };

    internal static McpServerTool UpdateAccount(bool success = true)
        => Fixed(
            new Tool
            {
                Name = "updateSobjectRecord",
                InputSchema = ObjectSchema(
                    ("sobject-name", StringProp),
                    ("id", StringProp),
                    ("body", ObjectProp)),
                OutputSchema = ObjectSchema(("success", BooleanProp)),
                Annotations = DestructiveAdmitted,
            },
            _ => Structured(new { success }));

    internal static McpServerTool SoqlQuery(string accountId, string description)
        => Fixed(
            new Tool
            {
                Name = "soqlQuery",
                InputSchema = ObjectSchema(("query", StringProp)),
                OutputSchema = ObjectSchema(("records", ArrayOfObjects)),
                Annotations = ReadOnlyAdmitted,
            },
            _ => Structured(new
            {
                records = new[]
                {
                    new { Id = accountId, Description = description },
                },
            }));

    private static JsonElement ObjectSchema(params (string Name, object Definition)[] properties)
    {
        var shape = new Dictionary<string, object?>
        {
            [Type] = Object,
            [Properties] = properties.ToDictionary(
                property => property.Name,
                property => property.Definition,
                StringComparer.Ordinal),
            [Required] = properties.Select(property => property.Name).ToArray(),
        };
        return JsonSerializer.SerializeToElement(shape);
    }

    private static CallToolResult Structured(object payload)
        => new()
        {
            StructuredContent = JsonSerializer.SerializeToElement(payload),
        };

    private static FixedSchemaTool Fixed(
        Tool protocolTool,
        Func<RequestContext<CallToolRequestParams>, CallToolResult> invoke)
        => new(protocolTool, invoke);

    private sealed class FixedSchemaTool(
        Tool protocolTool,
        Func<RequestContext<CallToolRequestParams>, CallToolResult> invoke) : McpServerTool
    {
        public override Tool ProtocolTool { get; } = protocolTool;

        public override IReadOnlyList<object> Metadata { get; } = [];

        public override ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(invoke(request));
        }
    }
}
