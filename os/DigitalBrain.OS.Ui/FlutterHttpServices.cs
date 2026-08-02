using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Client;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Flutter.Http;

internal static class FlutterHttpServices
{
    public const string BehaviorAuthorNeuronName = "behavior-author";

    public static IServiceCollection AddFlutterHttpServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(static services =>
            OwnerSessionJournal.Open(
                services.GetRequiredService<IGrainFactory>(),
                services.GetRequiredService<IDigitalBrain>().Owner));
        services.TryAddSingleton<BrainTopologyReader>();
        services.TryAddSingleton<IBehaviorAuthor>(static services =>
        {
            var unkeyed = services.GetService<IChatClient>();
            if (unkeyed is not null)
            {
                return BehaviorAuthor.ForChatClient(unkeyed);
            }

            var brain = services.GetRequiredService<IDigitalBrain>();
            return new BehaviorAuthor(async (messages, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var response = await brain
                    .GetGrainProxy<IGemma4>(BehaviorAuthorNeuronName)
                    .Respond([.. messages]);
                return response.Text ?? string.Empty;
            });
        });

        return services;
    }
}
