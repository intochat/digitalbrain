using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;
using DigitalBrain.Execution;

namespace DigitalBrain.SmartPrompt;

[GrainType("smartpromptrunner")]
internal sealed class SmartPromptRunner : Neuron, ISmartPromptRunner, IRemindable
{
    private const string ScheduleReminderName = "smart-prompt.schedule";

    private static readonly CapabilityId GmailSearch = CapabilityId.Parse("gmail.search");
    private static readonly CapabilityId SalesforceUpsert = CapabilityId.Parse("salesforce.upsert");
    private static readonly CapabilityId WebSearchCompany = CapabilityId.Parse("websearch.company");

    public async Task HandleAsync(RunSmartPrompt synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        // OfferChat kit card is intentionally deferred; run completes Execution first.
        var promptName = string.IsNullOrWhiteSpace(synapse.PromptName) ? Id.Name : synapse.PromptName.Trim();
        if (!string.Equals(promptName, Id.Name, StringComparison.Ordinal))
        {
            throw new NeuronAuthorizationException(
                $"Smart prompt runner '{Id}' refuses run for prompt '{promptName}'.");
        }

        await RunPromptAsync(synapse.CommandId, promptName, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task ScheduleSmartPrompt(TimeSpan period)
    {
        if (period < TimeSpan.FromMinutes(1))
        {
            throw new NeuronAuthorizationException(
                $"Smart prompt runner '{Id}' refuses schedule period shorter than one minute.");
        }

        await this.RegisterOrUpdateReminder(ScheduleReminderName, period, period)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        if (!string.Equals(reminderName, ScheduleReminderName, StringComparison.Ordinal))
        {
            return;
        }

        await RunPromptAsync(CommandId.New(), Id.Name, CancellationToken.None)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task RunPromptAsync(CommandId commandId, string promptName, CancellationToken cancellationToken)
    {
        var entity = GrainFactory.GetGrain<ISmartPrompt>(
            EntityId.For<ISmartPrompt>(Id.Owner, promptName).ToGrainId());
        var state = await entity.Read()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (state is null)
        {
            throw new NeuronAuthorizationException(
                $"Smart prompt '{promptName}' has no saved document.");
        }

        if (!state.Document.Enabled)
        {
            throw new NeuronAuthorizationException(
                $"Smart prompt '{promptName}' is disabled.");
        }

        if (state.ActiveRevisionId is not { } revisionId || revisionId == Guid.Empty)
        {
            throw new NeuronAuthorizationException(
                $"Smart prompt '{promptName}' has no active revision.");
        }

        var grants = MapGrants(state.Document.Bindings);
        var executionId = ExecutionId.New();
        var execution = GrainFactory.GetGrain<IExecution>(
            NeuronId.For<IExecution>(Id.Owner, executionId.ToString()).ToGrainId());

        await execution.HandleAsync(
                new StartExecution(
                    commandId,
                    executionId,
                    new SmartPromptWorkload(PromptIdFromName(promptName), revisionId, state.Document.BodyText),
                    ExecutionDriverKind.Agent,
                    grants),
                cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await EmitAsync(new SmartPromptRunStarted(commandId, promptName, executionId))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private static IReadOnlyList<CapabilityId> MapGrants(IReadOnlyList<SmartPromptBinding> bindings)
    {
        var grants = new List<CapabilityId>(bindings.Count);
        for (var i = 0; i < bindings.Count; i++)
        {
            var kind = bindings[i].Kind.Trim();
            if (kind.Equals("gmail", StringComparison.OrdinalIgnoreCase))
            {
                AddUnique(grants, GmailSearch);
            }
            else if (kind.Equals("salesforce", StringComparison.OrdinalIgnoreCase))
            {
                AddUnique(grants, SalesforceUpsert);
            }
            else if (kind.Equals("websearch", StringComparison.OrdinalIgnoreCase))
            {
                AddUnique(grants, WebSearchCompany);
            }
        }

        return grants;
    }

    private static void AddUnique(List<CapabilityId> grants, CapabilityId grant)
    {
        for (var i = 0; i < grants.Count; i++)
        {
            if (grants[i].Value == grant.Value)
            {
                return;
            }
        }

        grants.Add(grant);
    }

    private static Guid PromptIdFromName(string promptName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(promptName));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static void RequireCommand(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("A smart prompt run requires a command id.");
        }
    }
}
