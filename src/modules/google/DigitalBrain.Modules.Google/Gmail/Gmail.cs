using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Google;

internal sealed partial class Gmail :
    Neuron,
    IGmail,
    IHandle<GmailRequest>,
    IEmit<GmailResponse>
{
    internal const string GetMessageName = "get_message";
    private const string TokensName = "google.gmail.oauth";
    private const string ConfigurationRoot = "DigitalBrain:Google:Gmail";
    private static readonly McpServerDefinition DefaultServer = new(
        "google.gmail",
        "DigitalBrain Gmail",
        new Uri("https://gmailmcp.googleapis.com/mcp/v1"),
        ConfigurationRoot,
        ["https://www.googleapis.com/auth/gmail.readonly"]);

    private readonly McpRuntime _runtime;
    private readonly IDurableValue<byte[]> _tokenState;
    private readonly string _durableIdentity;
    private readonly McpServerDefinition _server;

    public Gmail(McpRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        _runtime = runtime;
        _tokenState = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(TokensName);
        _durableIdentity = Id.ToString();
        _server = ResolveServer(ServiceProvider.GetRequiredService<IConfiguration>());
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Planner/provider failures become a typed GmailResponse so directed request/reply does not retry forever.")]
    public async Task HandleAsync(GmailRequest synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await McpAuthorizationRail.EnsureAuthorizedAsync(
                GrainFactory,
                Id.Owner,
                ServiceProvider,
                TimeProvider,
                synapse.CommandId,
                _server,
                _tokenState,
                () => WriteStateAsync(),
                _durableIdentity,
                cancellationToken);

            var chat = ServiceProvider.GetRequiredService<IChatClient>();
            var messages = await _runtime.RunAsync(
                _server,
                _tokenState,
                () => WriteStateAsync(),
                _durableIdentity,
                synapse.CommandId,
                Id.Owner,
                GrainFactory,
                (client, callbackCancellation) => GmailPlanner.PlanAsync(
                    chat,
                    client,
                    _server,
                    synapse.Intent,
                    callbackCancellation),
                cancellationToken);

            await ReplyAsync(
                new GmailResponse(synapse.CommandId, synapse.Intent, messages),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (McpAuthorizationRequiredException)
        {
            // Delivery retries after the owner completes sign-in for the same CommandId.
            throw;
        }
        catch (Exception failure)
        {
            await ReplyAsync(
                new GmailResponse(
                    synapse.CommandId,
                    synapse.Intent,
                    [],
                    failure.Message),
                cancellationToken);
        }
    }

    private static McpServerDefinition ResolveServer(IConfiguration configuration)
    {
        var endpoint = configuration[$"{ConfigurationRoot}:{McpRuntimeHosting.EndpointConfigurationSuffix}"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return DefaultServer;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"{ConfigurationRoot}:{McpRuntimeHosting.EndpointConfigurationSuffix} must be an absolute URI.");
        }

        return DefaultServer.WithEndpoint(uri);
    }
}
