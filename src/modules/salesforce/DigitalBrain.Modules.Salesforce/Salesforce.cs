using DigitalBrain.Mcp;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Salesforce;

internal sealed partial class Salesforce : Neuron, ISalesforce
{
    private const string MutationsName = "salesforce.mutations";
    private const string QueryAccountName = "soqlQuery";
    private const string TokensName = "salesforce.oauth";
    private const string UpdateAccountName = "updateSobjectRecord";
    private static readonly McpServerDefinition Server = new(
        "salesforce",
        "DigitalBrain Salesforce",
        new Uri("https://api.salesforce.com/platform/mcp/v1/platform/sobject-mutations"),
        "DigitalBrain:Salesforce",
        ["mcp_api", "refresh_token"],
        requiresClientSecret: false);
    private static readonly TimeSpan ReconciliationTimeout = TimeSpan.FromSeconds(30);
    private readonly string _durableIdentity;
    private readonly IDurableDictionary<Guid, byte[]> _mutations;
    private readonly McpRuntime _runtime;
    private readonly Serializer<MutationData> _states;
    private readonly IDurableValue<byte[]> _tokenState;

    public Salesforce(McpRuntime runtime)
    {
        _runtime = runtime;
        _mutations = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<Guid, byte[]>>(
            MutationsName);
        _states = ServiceProvider.GetRequiredService<Serializer<MutationData>>();
        _tokenState = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(TokensName);
        _durableIdentity = Id.ToString();
    }
}
