using DigitalBrain.Runtime.Ui;
using DigitalBrain.Kernel.Gateway;
using Orleans.Streams;
using Orleans.Streams.Core;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Kernel.Visualization;

public interface ITimelineRelayGrain : IGrainWithGuidKey
{
    Task EnsureActivatedAsync();
}

// Listens on the global synapse timeline, filters RfwCards, and pushes them
// onto the kernel-silo-local HomeFeedBus. Lives only in the kernel project,
// so Orleans places it on the kernel silo (no other silo's assembly graph
// declares this grain type). One activation per cluster (Guid.Empty key).
[ImplicitStreamSubscription(Neuron.GlobalTimelineNamespace)]
public sealed class TimelineRelayGrain(HomeFeedBus bus, ILogger<TimelineRelayGrain> logger)
    : Grain, ITimelineRelayGrain, IStreamSubscriptionObserver, IAsyncObserver<Synapse>
{
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        var streamProvider = this.GetStreamProvider(Neuron.SynapseStreamProvider);
        var stream = streamProvider.GetStream<Synapse>(
            StreamId.Create(Neuron.GlobalTimelineNamespace, Guid.Empty));
        await stream.SubscribeAsync(this);
    }

    public Task EnsureActivatedAsync() => Task.CompletedTask;

    public async Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory)
    {
        var handle = handleFactory.Create<Synapse>();
        try
        {
            await handle.ResumeAsync(this);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Resuming subscription with cached token failed in TimelineRelayGrain. Falling back to fresh subscribe.");
            await handle.ResumeAsync(this, null);
        }
    }

    public async Task OnNextAsync(Synapse item, StreamSequenceToken? token = null)
    {
        var typeName = item.GetType().Name;
        var typeFullName = item.GetType().FullName;
        if (typeFullName == "DigitalBrain.Runtime.Neurons.QuerySynapse" || 
            typeName == "PingRequest" || 
            typeName == "PingResponse" || 
            typeName == "TestSynapse")
        {
            return;
        }

        RfwCard? card = null;
        if (item is RfwCard directCard)
        {
            card = directCard;
        }
        else if (item.GetType().FullName == typeof(RfwCard).FullName)
        {
            try
            {
                var type = item.GetType();
                var libraryName = type.GetProperty("LibraryName")?.GetValue(item) as string ?? "";
                var rootWidget = type.GetProperty("RootWidget")?.GetValue(item) as string ?? "";
                var dataJson = type.GetProperty("DataJson")?.GetValue(item) as string ?? "";
                
                card = new RfwCard(
                    LibraryName: libraryName,
                    RootWidget: rootWidget,
                    DataJson: dataJson
                )
                {
                    Headers = item.Headers
                };
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to extract RfwCard properties from dynamic/proxy type {Type}.", item.GetType().FullName);
            }
        }

        if (card != null)
        {
            await bus.BroadcastAsync(card);
            if (!string.IsNullOrEmpty(card.CallerNeuronType))
            {
                try
                {
                    var store = GrainFactory.GetGrain<DigitalBrain.Runtime.Runtime.INeuronRfwStoreGrain>(card.CallerNeuronType);
                    await store.SaveLatestCardAsync(new DigitalBrain.Runtime.Runtime.PersistedRfwCard
                    {
                        CorrelationId = card.CorrelationId.ToString(),
                        LibraryName = card.LibraryName,
                        RootWidget = card.RootWidget,
                        DataJson = card.DataJson,
                        Timestamp = card.Timestamp.ToString("O"),
                        CallerNeuronType = card.CallerNeuronType
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to persist dynamic RfwCard for neuron type {NeuronType}", card.CallerNeuronType);
                }
            }
        }
        else if (item.GetType().FullName == "DigitalBrain.SDK.Google.OAuthConsentRequired")
        {
            try
            {
                var type = item.GetType();
                var userAccountId = type.GetProperty("UserAccountId")?.GetValue(item) as string ?? "default";
                var consentUrl = type.GetProperty("ConsentUrl")?.GetValue(item) as string ?? "https://accounts.google.com/o/oauth2/auth";
                var scopesObj = type.GetProperty("Scopes")?.GetValue(item);
                var scopesList = new List<string>();
                if (scopesObj is System.Collections.IEnumerable enumerable)
                {
                    foreach (var sc in enumerable)
                    {
                        if (sc is string s) scopesList.Add(s);
                    }
                }

                var googleAuthSource =
                    "import digitalbrain;\n" +
                    "\n" +
                    "widget root = Panel(\n" +
                    "  padding: 24.0,\n" +
                    "  child: VStack(\n" +
                    "    gap: 16.0,\n" +
                    "    cross: \"start\",\n" +
                    "    children: [\n" +
                    "      Text(text: \"Google Cloud Authorization Required\", variant: \"title\"),\n" +
                    "      Text(text: \"DigitalBrain needs your permission to access Google Cloud / Gmail services.\", variant: \"body\"),\n" +
                    "      Text(text: \"Requested Scopes:\", variant: \"dim\"),\n" +
                    "      ...for scope in data.scopes:\n" +
                    "        Text(text: \"• \" + scope, variant: \"body\"),\n" +
                    "      Panel(\n" +
                    "        padding: 12.0,\n" +
                    "        child: Button(\n" +
                    "          label: \"Authorize Google Account\",\n" +
                    "          onTap: event \"openUrl\" { url: data.consentUrl }\n" +
                    "        ),\n" +
                    "      ),\n" +
                    "    ],\n" +
                    "  ),\n" +
                    ");\n";

                var dataJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    userAccountId = userAccountId,
                    consentUrl = consentUrl,
                    scopes = scopesList,
                    source = googleAuthSource
                });

                var googleCard = new RfwCard(
                    LibraryName: "digitalbrain",
                    RootWidget: "GoogleAuthCard",
                    DataJson: dataJson
                )
                {
                    Headers = SynapseMetadata.Create(
                        synapseId: item.SynapseId,
                        correlationId: item.CorrelationId,
                        causationId: item.CausationId,
                        callerNeuronId: item.CallerNeuronId,
                        callerNeuronType: item.CallerNeuronType,
                        receiverNeuronId: item.ReceiverNeuronId,
                        receiverNeuronType: item.ReceiverNeuronType,
                        timestamp: item.Timestamp
                    )
                };

                await bus.BroadcastAsync(googleCard);

                var callerType = string.IsNullOrEmpty(googleCard.CallerNeuronType) ? "DigitalBrain.SDK.Google.Gmail.GmailNeuron" : googleCard.CallerNeuronType;
                try
                {
                    var store = GrainFactory.GetGrain<DigitalBrain.Runtime.Runtime.INeuronRfwStoreGrain>(callerType);
                    await store.SaveLatestCardAsync(new DigitalBrain.Runtime.Runtime.PersistedRfwCard
                    {
                        CorrelationId = googleCard.CorrelationId.ToString(),
                        LibraryName = googleCard.LibraryName,
                        RootWidget = googleCard.RootWidget,
                        DataJson = googleCard.DataJson,
                        Timestamp = googleCard.Timestamp.ToString("O"),
                        CallerNeuronType = callerType
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to persist GoogleAuthCard for neuron type {NeuronType}", callerType);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to map OAuthConsentRequired to GoogleAuthCard.");
            }
        }
        else
        {
            var serialized = System.Text.Json.JsonSerializer.Serialize(item, item.GetType());
            var broadcastCard = new RfwCard(LibraryName: "synapse-broadcast",
        RootWidget: item.GetType().FullName!,
        DataJson: serialized) { Headers = SynapseMetadata.Create(
            synapseId: item.SynapseId,
            correlationId: item.CorrelationId,
            causationId: item.CausationId,
            callerNeuronId: item.CallerNeuronId,
            callerNeuronType: item.CallerNeuronType,
            receiverNeuronId: item.ReceiverNeuronId,
            receiverNeuronType: item.ReceiverNeuronType,
            timestamp: item.Timestamp
        ) };
            await bus.BroadcastAsync(broadcastCard);
            if (!string.IsNullOrEmpty(broadcastCard.CallerNeuronType))
            {
                try
                {
                    var store = GrainFactory.GetGrain<DigitalBrain.Runtime.Runtime.INeuronRfwStoreGrain>(broadcastCard.CallerNeuronType);
                    await store.SaveLatestCardAsync(new DigitalBrain.Runtime.Runtime.PersistedRfwCard
                    {
                        CorrelationId = broadcastCard.CorrelationId.ToString(),
                        LibraryName = broadcastCard.LibraryName,
                        RootWidget = broadcastCard.RootWidget,
                        DataJson = broadcastCard.DataJson,
                        Timestamp = broadcastCard.Timestamp.ToString("O"),
                        CallerNeuronType = broadcastCard.CallerNeuronType
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to persist broadcast card for neuron type {NeuronType}", broadcastCard.CallerNeuronType);
                }
            }
        }
    }

    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task OnErrorAsync(Exception ex)
    {
        if (ex is QueueCacheMissException || ex.GetType().FullName == "Orleans.Streams.QueueCacheMissException")
        {
            logger.LogWarning(ex, "Transient stream cache miss in TimelineRelayGrain; Orleans pulling agent will recover.");
        }
        else
        {
            logger.LogError(ex, "Timeline relay stream subscription error.");
        }
        return Task.CompletedTask;
    }
}
