using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors;

internal sealed class HttpBehaviorHostBrokerClientFactory : IBehaviorHostBrokerClientFactory
{
    private readonly IHttpClientFactory httpClientFactory;

    public HttpBehaviorHostBrokerClientFactory(IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        this.httpClientFactory = httpClientFactory;
    }

    public IBehaviorHostBrokerClient Create(OwnerId owner, NeuronId task, AttemptId attempt)
        => new HttpBehaviorHostBrokerClient(
            httpClientFactory.CreateClient(BehaviorHostHosting.BrokerHttpClientName),
            owner,
            task,
            attempt);
}
