using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Abstractions.Behaviors;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

internal sealed class BehaviorTools(IGrainFactory grains, INeuronInvoker invoker)
{
    internal static readonly string[] Names = ["behavior_catalog", "behavior_save", "behavior_start", "behavior_stop", "behavior_read", "behavior_list"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    internal IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(async ([Description("Optional typed neuron to inspect as a source or action target, such as chart:desk")] string? neuron = null) =>
        {
            var capabilities = await Behavior("catalog").Catalog().ConfigureAwait(false);
            var contracts = invoker.Describe(new NeuronId("behavior", "catalog"));
            var target = neuron is null ? [] : invoker.Describe(Parse(neuron));
            return JsonSerializer.Serialize(new { capabilities, contracts, target }, Json);
        }, "behavior_catalog", "Discover registered behavior capabilities, definition schemas and optional integration methods. Behaviors are durable graphs. Filters, mappings and tool-free decisions are owned processors; sources are shared. Discover contracts before composing.");

        yield return AIFunctionFactory.Create(async (
            [Description("Stable behavior name, without spaces")] string name,
            [Description("JSON BehaviorDefinition matching behavior_catalog schemas")] string definition,
            [Description("Stable command GUID; reuse on retry")] string commandId,
            CancellationToken cancellationToken) =>
        {
            var value = JsonSerializer.Deserialize(definition, BehaviorJson.Default.BehaviorDefinition)
                ?? throw new ArgumentException("Definition is required.");
            var behavior = Behavior(name);
            await behavior.Save(new(value, Command(commandId))).ConfigureAwait(false);
            return await InspectAfterWork(behavior, cancellationToken).ConfigureAwait(false);
        }, "behavior_save", "Validate and persist a behavior definition while stopped. Never writes executable code or configures a real external account. On validation failure, fix the definition before starting.");

        yield return AIFunctionFactory.Create(async (string name, string commandId, CancellationToken cancellationToken) =>
        {
            var behavior = Behavior(name);
            await behavior.Start(new(Command(commandId))).ConfigureAwait(false);
            return await InspectAfterWork(behavior, cancellationToken).ConfigureAwait(false);
        }, "behavior_start", "Activate a saved validated behavior. Report success only when returned status is Running; any error is actionable. Reuse commandId on retry.");

        yield return AIFunctionFactory.Create(async (string name, string commandId, CancellationToken cancellationToken) =>
        {
            var behavior = Behavior(name);
            await behavior.Stop(new(Command(commandId))).ConfigureAwait(false);
            return await InspectAfterWork(behavior, cancellationToken).ConfigureAwait(false);
        }, "behavior_stop", "Stop a saved behavior and remove only its connections. Shared resources retain state and other subscriptions. Reuse commandId on retry.");

        yield return AIFunctionFactory.Create(async (string name) => await Inspect(Behavior(name)).ConfigureAwait(false),
            "behavior_read", "Read saved definition, active node identities, lifecycle and processor diagnostics. An uncertain external action is paused, never silently retried.");
        yield return AIFunctionFactory.Create(async () => JsonSerializer.Serialize(await Behavior("catalog").List().ConfigureAwait(false), Json),
            "behavior_list", "List saved behavior identities, including stopped behaviors. Read a behavior for its status and definition.");
    }

    private static CommandId Command(string text) => CommandId.TryParse(text, out var id) ? id : throw new ArgumentException("commandId must be a nonempty GUID.");
    private static NeuronId Parse(string text) => NeuronId.TryParse(text, out var id) ? id : throw new ArgumentException("Invalid neuron identity.");
    private IBehavior Behavior(string name) => grains.GetGrain<IBehavior>(new NeuronId("behavior", name).ToGrainId());

    private static async Task<string> InspectAfterWork(IBehavior behavior, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (await behavior.ReadPendingCount().ConfigureAwait(false) > 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }
        return await Inspect(behavior).ConfigureAwait(false);
    }

    private static async Task<string> Inspect(IBehavior behavior)
        => JsonSerializer.Serialize(new { behavior = await behavior.Read().ConfigureAwait(false), diagnostics = await behavior.Diagnostics().ConfigureAwait(false) }, Json);
}
