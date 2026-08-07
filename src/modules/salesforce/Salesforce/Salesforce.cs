using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Salesforce;

internal sealed partial class Salesforce :
    Neuron,
    ISalesforce,
    IHandle<SalesforceRequest>,
    IHandle<ApproveSalesforceMutation>,
    IEmit<SalesforceResponse>
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
    private SynapseDelivery? _activeDelivery;

    public Salesforce(McpRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
        _mutations = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<Guid, byte[]>>(MutationsName);
        _states = ServiceProvider.GetRequiredService<Serializer<MutationData>>();
        _tokenState = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(TokensName);
        _durableIdentity = Id.ToString();
    }

    Task INeuron.Deliver(SynapseDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        _activeDelivery = delivery;
        return base.Deliver(delivery, cancellationToken);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Planner/provider failures become a typed SalesforceResponse so directed request/reply does not retry forever.")]
    public async Task HandleAsync(SalesforceRequest synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (synapse.IsAccountDescriptionProposal)
            {
                var proposed = await ProposeAccountDescriptionAsync(
                    synapse.CommandId,
                    ISessionNeuron.ForOwner(Id.Owner),
                    synapse.AccountId!,
                    synapse.Description!,
                    cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                await ReplyAsync(
                    new SalesforceResponse(synapse.CommandId, synapse.Intent, proposed),
                    cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                return;
            }

            await McpAuthorizationRail.EnsureAuthorizedAsync(
                GrainFactory,
                Id.Owner,
                ServiceProvider,
                TimeProvider,
                synapse.CommandId,
                Server,
                _tokenState,
                () => WriteStateAsync(),
                _durableIdentity,
                cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            var chat = ServiceProvider.GetRequiredService<IChatClient>();
            _ = await _runtime.RunAsync(
                Server,
                _tokenState,
                () => WriteStateAsync(),
                _durableIdentity,
                synapse.CommandId,
                Id.Owner,
                GrainFactory,
                (client, callbackCancellation) => SalesforcePlanner.PlanReadAsync(
                    chat,
                    client,
                    Server,
                    synapse.Intent,
                    callbackCancellation),
                cancellationToken).ConfigureAwait(true);

            await ReplyAsync(
                new SalesforceResponse(synapse.CommandId, synapse.Intent),
                cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (McpAuthorizationRequiredException)
        {
            throw;
        }
        catch (Exception failure)
        {
            await ReplyAsync(
                new SalesforceResponse(synapse.CommandId, synapse.Intent, null, failure.Message),
                cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Approval failures become a typed SalesforceResponse; uncertain provider outcomes stay durable.")]
    public async Task HandleAsync(ApproveSalesforceMutation synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        ArgumentNullException.ThrowIfNull(synapse.Approval);
        cancellationToken.ThrowIfCancellationRequested();

        var approval = synapse.Approval;
        try
        {
            var evidence = CurrentApprovalEvidence(approval);
            var mutation = await ApproveAccountDescriptionAsync(approval, evidence, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            await ReplyAsync(
                new SalesforceResponse(
                    approval.CommandId,
                    "Approve Salesforce mutation",
                    mutation),
                cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (McpAuthorizationRequiredException)
        {
            throw;
        }
        catch (Exception failure)
        {
            await ReplyAsync(
                new SalesforceResponse(
                    approval.CommandId,
                    "Approve Salesforce mutation",
                    null,
                    failure.Message),
                cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    private SynapseDelivery CurrentApprovalEvidence(SalesforceMutationApproval approval)
    {
        var evidence = _activeDelivery
            ?? throw new InvalidOperationException(
                "Salesforce approval requires an active delivery context.");
        if (evidence.Caller != approval.Approver)
        {
            throw new NeuronAuthorizationException(
                "Salesforce mutation approval must be issued by this owner's human session.");
        }

        return evidence;
    }
}
