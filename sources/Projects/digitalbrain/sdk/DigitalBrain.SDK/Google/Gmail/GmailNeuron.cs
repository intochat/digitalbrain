using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.SDK.Google.Auth;
using Orleans.Journaling;

namespace DigitalBrain.SDK.Google.Gmail;

[GrainType(NeuronTargetFqn)]
[ImplicitStreamSubscription(GmailNeuronType)]
internal sealed class GmailNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    IGoogleAuthBroker broker,
    IGmailService gmailService,
    ILogger<GmailNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      IGmail,
      INeuronMetadata,
      IExternalNeuron,
      IHandle<GetLastNGmailSendersRequest>,
      IHandle<ConnectGmailRequest>
{
    public const string NeuronTargetFqn = "DigitalBrain.SDK.Google.Gmail.GmailNeuron";
    public const string GmailNeuronType = nameof(GmailNeuron);
    static readonly string[] Scopes = ["https://www.googleapis.com/auth/gmail.readonly"];
    const string DefaultConsentUrl = "https://accounts.google.com/o/oauth2/auth";

    public static NeuronId Id => new("google/gmail");
    public static string Icon => "gmail";
    public static NeuronCapability Capabilities => NeuronCapability.External;

    public async Task<string> AskAsync(string prompt)
    {
        Console.WriteLine($"[DIAGNOSTIC-GMAIL] AskAsync called with prompt: '{prompt}'. CorrelationId = '{Orleans.Runtime.RequestContext.Get("DigitalBrain.CorrelationId")}', ActiveScope = '{Orleans.Runtime.RequestContext.Get("DigitalBrain.ActiveScope")}'");
        var trimmed = (prompt ?? "").Trim();
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        var arg = parts.Length > 1 ? parts[1] : "";

        if (verb == "fetch")
        {
            int n = 10;
            if (int.TryParse(arg, out var parsedN)) n = parsedN;

            var userAccountId = "default";
            var hasToken = await broker.HasStoredTokenAsync(userAccountId, Scopes, CancellationToken.None);
            if (!hasToken)
            {
                var correlationId = Orleans.Runtime.RequestContext.Get("DigitalBrain.CorrelationId") is Guid cId ? cId : Guid.NewGuid();
                await FireSynapseAsync(new OAuthConsentRequired(UserAccountId: userAccountId,
            ConsentUrl: DefaultConsentUrl,
            Scopes: Scopes) { Headers = SynapseMetadata.Create(
                synapseId: Guid.NewGuid(),
                correlationId: correlationId,
                causationId: Guid.NewGuid(),
                callerNeuronId: default,
                callerNeuronType: null,
                receiverNeuronId: Guid.Empty,
                receiverNeuronType: "GatewayNeuron",
                timestamp: DateTimeOffset.UtcNow
            ) });
                return "OAuthConsentRequired";
            }

            var senders = await gmailService.ListRecentSendersAsync(userAccountId, n, CancellationToken.None);
            var sb = new System.Text.StringBuilder();
            foreach (var sender in senders)
            {
                sb.AppendLine($"- From: {sender.Name} <{sender.EmailAddress}>, Subject: {sender.Subject}, Received: {sender.ReceivedUtc:O}");
            }
            return sb.ToString();
        }

        return "Error: Unknown verb. Expected 'fetch <N>'.";
    }

    protected override async Task HandleSynapseAsync(Synapse synapse)
    {
        switch (synapse)
        {
            case ConnectGmailRequest connect:        await HandleConnectAsync(connect); break;
            case GetLastNGmailSendersRequest fetch:  await HandleFetchAsync(fetch);     break;
        }
    }

    async Task HandleConnectAsync(ConnectGmailRequest request)
    {
        await broker.AuthorizeAsync(request.UserAccountId, Scopes, CancellationToken.None);
        Logger.LogInformation("Gmail connected for {User}", request.UserAccountId);

        await FireSynapseAsync(new GmailConnected(UserAccountId: request.UserAccountId) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: request.CorrelationId,
            causationId: request.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: request.CallerNeuronId,
            receiverNeuronType: request.CallerNeuronType ?? "External",
            timestamp: default
        ) });
    }

    async Task HandleFetchAsync(GetLastNGmailSendersRequest request)
    {
        var hasToken = await broker.HasStoredTokenAsync(
            request.UserAccountId, Scopes, CancellationToken.None);
        if (!hasToken)
        {
            await FireSynapseAsync(new OAuthConsentRequired(UserAccountId: request.UserAccountId,
        ConsentUrl: DefaultConsentUrl,
        Scopes: Scopes) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: request.CorrelationId,
            causationId: request.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: request.CallerNeuronId,
            receiverNeuronType: request.CallerNeuronType ?? "External",
            timestamp: default
        ) });
            return;
        }

        var senders = await gmailService.ListRecentSendersAsync(
            request.UserAccountId, request.N, CancellationToken.None);

        await FireSynapseAsync(new GmailSendersReady(UserAccountId: request.UserAccountId,
        Senders: senders) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: request.CorrelationId,
            causationId: request.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: request.CallerNeuronId,
            receiverNeuronType: request.CallerNeuronType ?? "External",
            timestamp: default
        ) });
    }
}
