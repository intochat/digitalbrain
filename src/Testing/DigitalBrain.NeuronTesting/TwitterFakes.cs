using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orleans;

namespace DigitalBrain.Testing;

public interface ITwitterAccount : INeuron
{
    Task Post(string text);
}

public interface IBitcoin : INeuron
{
    Task SetPrice(decimal usd);
    Task<decimal> GetPrice();
}

public interface INotification : INeuron
{
    Task Notify(string message);
    Task<IReadOnlyList<string>> Read();
}

[GenerateSerializer, Alias("test.posted")]
public sealed record Posted([property: Id(0)] string From, [property: Id(1)] string Text) : Signal;

public sealed record TweetWebhook(string Account, string Text);

public sealed class TestTwitterModule : IModule
{
    public void Configure(ISiloBuilder builder) { }

    public void Configure(IEndpointRouteBuilder endpoints)
        => endpoints.MapPost("/twitter/webhook", async (TweetWebhook tweet, IGrainFactory grains) =>
        {
            await grains.GetGrain<ITwitterAccount>(tweet.Account).Post(tweet.Text);
            return Results.Accepted();
        });
}

[GrainType("twitter")]
public sealed class TwitterAccount : Neuron, ITwitterAccount
{
    public Task Post(string text) => PublishAsync(new Posted(this.GetPrimaryKeyString(), text));
}

[GrainType("bitcoin")]
public sealed class Bitcoin : Neuron, IBitcoin
{
    private decimal _usd;
    public Task SetPrice(decimal usd) { _usd = usd; return Task.CompletedTask; }
    public Task<decimal> GetPrice() => Task.FromResult(_usd);
}

[GrainType("notification")]
public sealed class Notification : Neuron, INotification
{
    private readonly List<string> _sent = [];
    public Task Notify(string message) { _sent.Add(message); return Task.CompletedTask; }
    public Task<IReadOnlyList<string>> Read() => Task.FromResult<IReadOnlyList<string>>(_sent.ToArray());
}
