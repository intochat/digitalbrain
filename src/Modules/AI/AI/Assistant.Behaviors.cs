using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Product.Interactions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Assistant;

internal sealed partial class Assistant
{
    // Mutations use this neuron's send path, so admission is real graph traffic.
    // Queries use the kernel read surface; calling IDigitalBrain here would re-enter the root.
    private IReadOnlyList<AIFunction> BehaviorTools()
    {
        if (AgentTurnContext.Current is not { } turn || turn.Chat.Owner != Id.Owner)
        {
            return [];
        }

        var principal = turn.Actor.PrincipalId;

        async Task<string> Example(
            [Description("The example name: github-pr-review or personal-code-review")] string name,
            CancellationToken cancellationToken)
        {
            if (name is not ("github-pr-review" or "personal-code-review"))
            {
                return "Available examples: github-pr-review, personal-code-review.";
            }

            using var stream = typeof(Assistant).Assembly.GetManifestResourceStream($"DigitalBrain.Examples.{name}.csx")
                ?? throw new InvalidOperationException("The requested behavior example is unavailable.");
            using var reader = new StreamReader(stream);
            var source = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(true);
            return source.Replace("\"__CHAT_INSTANCE__\"", JsonSerializer.Serialize(turn.Chat.Name), StringComparison.Ordinal);
        }

        async Task<string> Admit(
            [Description("A short stable behavior name without spaces, e.g. personal-code-review")] string name,
            [Description("Complete C# script source using Brain and CancellationToken globals")] string source,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scoped = PrincipalPartition.InstanceName(principal, name);
            var result = await SendAsync(BehaviorsId, new AdmitBehavior(scoped, source)).ConfigureAwait(true);
            if (result.Outcome != DeliveryOutcome.Handled)
            {
                return $"Behavior admission was {result.Outcome}; it was not saved.";
            }

            return await Read(name, cancellationToken).ConfigureAwait(true);
        }

        async Task<string> List(CancellationToken cancellationToken)
        {
            var definitions = await ReadBehaviorDefinitionsAsync(cancellationToken).ConfigureAwait(true);
            return JsonSerializer.Serialize(definitions.Where(Owned).Select(definition => new
            {
                Name = LocalName(definition),
                definition.Revision,
                Status = definition.Status.ToString(),
                definition.Summary,
                definition.Diagnostics,
            }));
        }

        async Task<string> Read(
            [Description("The saved behavior name")] string name,
            CancellationToken cancellationToken)
        {
            var scoped = PrincipalPartition.InstanceName(principal, name);
            var definitions = await ReadBehaviorDefinitionsAsync(cancellationToken).ConfigureAwait(true);
            var definition = definitions.SingleOrDefault(candidate => candidate.Name == scoped && Owned(candidate));
            return definition is null ? $"No saved behavior named '{name}'." : JsonSerializer.Serialize(new
            {
                Name = LocalName(definition),
                definition.Source,
                definition.Revision,
                Status = definition.Status.ToString(),
                definition.Summary,
                definition.Diagnostics,
            });
        }

        async Task<string> Remove(
            [Description("The saved behavior name to remove")] string name,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scoped = PrincipalPartition.InstanceName(principal, name);
            var definitions = await ReadBehaviorDefinitionsAsync(cancellationToken).ConfigureAwait(true);
            if (!definitions.Any(definition => definition.Name == scoped && Owned(definition)))
            {
                return $"No saved behavior named '{name}'.";
            }

            var result = await SendAsync(BehaviorsId, new RemoveBehavior(scoped)).ConfigureAwait(true);
            return result.Outcome == DeliveryOutcome.Handled
                ? $"Removed saved behavior '{name}'. Cancellation of its running script is requested; "
                    + "scripts must observe CancellationToken to stop."
                : $"Behavior removal was {result.Outcome}.";
        }

        bool Owned(BehaviorDefinition definition)
            => definition.Principal == principal && PrincipalPartition.OwnsInstance(principal, definition.Name);

        static string LocalName(BehaviorDefinition definition)
        {
            _ = PrincipalPartition.TryParse(definition.Name, out _, out var localName);
            return localName;
        }

        return
        [
            AIFunctionFactory.Create(Example, new AIFunctionFactoryOptions
            {
                Name = "read_behavior_example",
                Description = "Read the supported C# source for github-pr-review or personal-code-review with this chat as destination. "
                    + "Customize the exact source before admission; unresolved placeholders must be supplied from configuration and user requirements. "
                    + "The GitHub example waits for required green CI, runs architecture and quality agents, and publishes in DigitalBrain chat.",
            }),
            AIFunctionFactory.Create(Admit, new AIFunctionFactoryOptions
            {
                Name = "admit_behavior",
                Description = "Save or replace the user's named C# behavior for the separate scripting worker. "
                    + "Each admission starts a new revision. Returns current source/status; Admitted means "
                    + "saved and awaiting compilation, not confirmed running. Use for requested custom automation.",
            }),
            AIFunctionFactory.Create(List, new AIFunctionFactoryOptions
            {
                Name = "list_behaviors",
                Description = "List this user's saved C# behaviors with their current execution status and diagnostics.",
            }),
            AIFunctionFactory.Create(Read, new AIFunctionFactoryOptions
            {
                Name = "read_behavior",
                Description = "Read this user's exact saved C# source, revision, status and diagnostics before editing a behavior.",
            }),
            AIFunctionFactory.Create(Remove, new AIFunctionFactoryOptions
            {
                Name = "remove_behavior",
                Description = "Remove this user's saved C# behavior and request cancellation of its running script.",
            }),
        ];
    }

    private NeuronId BehaviorsId => NeuronId.For<IBehaviors>(Id.Owner, "default");

    private Task<IReadOnlyList<BehaviorDefinition>> ReadBehaviorDefinitionsAsync(CancellationToken cancellationToken)
        => GrainFactory.GetGrain<IBehaviorsKernel>(BehaviorsId.ToGrainId()).ReadCurrent().WaitAsync(cancellationToken);
}
