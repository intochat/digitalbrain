using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Google.Auth;
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
    IHandle<GmailSearchRequest>,
    IHandle<GmailGetMessageRequest>,
    IEmit<GmailResponse>,
    IEmit<GmailSearchResponse>,
    IEmit<GmailGetMessageResponse>
{
    private const string TokensName = "google.gmail.oauth";
    private readonly IDurableValue<byte[]> _tokenState;
    private readonly IDurableDictionary<Guid, string> _pendingAuthStates;
    private readonly string _durableIdentity;
    private readonly string _userKey;
    private GoogleSignIn? _signIn;
    private GmailProvider? _provider;

    public Gmail()
    {
        _tokenState = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(TokensName);
        _pendingAuthStates = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<Guid, string>>(
            GmailAuthRail.PendingStatesName);
        _durableIdentity = Id.ToString();
        _userKey = Id.Name;
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
            var provider = await EnsureReadyAsync(synapse.CommandId, cancellationToken);
            var chat = ServiceProvider.GetRequiredService<IChatClient>();
            var catalog = SdkCatalogAdmission.Build(provider.Service);
            if (catalog.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{GmailAuthRail.ServerDisplayName} catalog has no admitted read-only tools.");
            }

            var messages = await GmailPlanner.PlanAsync(
                chat,
                catalog,
                synapse.Intent,
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

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Typed op failures become a typed response so directed request/reply does not retry forever.")]
    public async Task HandleAsync(GmailSearchRequest synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var provider = await EnsureReadyAsync(synapse.CommandId, cancellationToken);
            var headers = await provider.SearchAsync(
                synapse.Query,
                synapse.MaxResults,
                cancellationToken);
            await ReplyAsync(
                new GmailSearchResponse(synapse.CommandId, headers),
                cancellationToken);
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
                new GmailSearchResponse(synapse.CommandId, [], failure.Message),
                cancellationToken);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Typed op failures become a typed response so directed request/reply does not retry forever.")]
    public async Task HandleAsync(GmailGetMessageRequest synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var provider = await EnsureReadyAsync(synapse.CommandId, cancellationToken);
            var message = await provider.GetMessageAsync(synapse.MessageId, cancellationToken);
            await ReplyAsync(
                new GmailGetMessageResponse(synapse.CommandId, message),
                cancellationToken);
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
                new GmailGetMessageResponse(synapse.CommandId, null, failure.Message),
                cancellationToken);
        }
    }

    private async Task<GmailProvider> EnsureReadyAsync(CommandId commandId, CancellationToken cancellationToken)
    {
        await GmailAuthRail.EnsureAuthorizedAsync(
            GrainFactory,
            Id.Owner,
            ServiceProvider,
            commandId,
            _tokenState,
            _pendingAuthStates,
            () => WriteStateAsync(),
            _durableIdentity,
            _userKey,
            cancellationToken,
            TimeProvider);

        if (_provider is not null)
        {
            try
            {
                if (_provider.Service.HttpClientInitializer is global::Google.Apis.Auth.OAuth2.UserCredential cached)
                {
                    _ = await cached.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
                }

                return _provider;
            }
            catch (global::Google.Apis.Auth.OAuth2.Responses.TokenResponseException)
            {
                _provider = null;
                if (_signIn is not null)
                {
                    await _signIn.DisposeAsync();
                    _signIn = null;
                }

                var store = new DigitalBrain.Google.Auth.DurableGoogleTokenStore(
                    _tokenState,
                    () => WriteStateAsync(),
                    ServiceProvider.GetRequiredService<DigitalBrain.Security.IDurablePayloadProtector>(),
                    DigitalBrain.Google.Auth.DurableGoogleTokenStore.Purpose(
                        GmailAuthRail.ServerKey,
                        _durableIdentity));
                await store.DeleteAsync<global::Google.Apis.Auth.OAuth2.Responses.TokenResponse>(_userKey);
                await GmailAuthRail.EnsureAuthorizedAsync(
                    GrainFactory,
                    Id.Owner,
                    ServiceProvider,
                    commandId,
                    _tokenState,
                    _pendingAuthStates,
                    () => WriteStateAsync(),
                    _durableIdentity,
                    _userKey,
            cancellationToken,
            TimeProvider);
                throw new InvalidOperationException("Gmail authorization recovery did not park after token failure.");
            }
        }

        if (_signIn is not null)
        {
            await _signIn.DisposeAsync();
            _signIn = null;
        }

        try
        {
            _signIn = await GmailAuthRail.CreateSignInAsync(
                ServiceProvider,
                _tokenState,
                () => WriteStateAsync(),
                _durableIdentity,
                cancellationToken,
                TimeProvider);

            var configuration = ServiceProvider.GetRequiredService<IConfiguration>();
            var baseUri = ResolveBaseUri(configuration);
            var service = await _signIn.CreateServiceAsync(_userKey, cancellationToken, baseUri);
            try
            {
                // Force credential materialization so permanent refresh failures re-park.
                if (service.HttpClientInitializer is global::Google.Apis.Auth.OAuth2.UserCredential credential)
                {
                    _ = await credential.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
                }

                _provider = new GmailProvider(service);
                service = null;
                return _provider;
            }
            finally
            {
                service?.Dispose();
            }
        }
        catch (global::Google.Apis.Auth.OAuth2.Responses.TokenResponseException)
        {
            if (_signIn is not null)
            {
                await _signIn.DisposeAsync();
                _signIn = null;
            }

            _provider = null;
            var store = new DigitalBrain.Google.Auth.DurableGoogleTokenStore(
                _tokenState,
                () => WriteStateAsync(),
                ServiceProvider.GetRequiredService<DigitalBrain.Security.IDurablePayloadProtector>(),
                DigitalBrain.Google.Auth.DurableGoogleTokenStore.Purpose(
                    GmailAuthRail.ServerKey,
                    _durableIdentity));
            await store.DeleteAsync<global::Google.Apis.Auth.OAuth2.Responses.TokenResponse>(_userKey);
            await GmailAuthRail.EnsureAuthorizedAsync(
                GrainFactory,
                Id.Owner,
                ServiceProvider,
                commandId,
                _tokenState,
                _pendingAuthStates,
                () => WriteStateAsync(),
                _durableIdentity,
                _userKey,
            cancellationToken,
            TimeProvider);
            throw new InvalidOperationException("Gmail authorization recovery did not park after token failure.");
        }
    }

    private static string? ResolveBaseUri(IConfiguration configuration)
    {
        var baseUri = configuration[$"{GoogleOAuthOptions.ConfigurationRoot}:BaseUri"];
        if (string.IsNullOrWhiteSpace(baseUri))
        {
            baseUri = configuration[$"{GoogleOAuthOptions.ConfigurationRoot}:Endpoint"];
        }

        if (string.IsNullOrWhiteSpace(baseUri))
        {
            return null;
        }

        if (!Uri.TryCreate(baseUri, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{GoogleOAuthOptions.ConfigurationRoot}:BaseUri must be an absolute http(s) URI.");
        }

        if (!IsLoopbackOrHttps(uri))
        {
            throw new InvalidOperationException(
                $"{GoogleOAuthOptions.ConfigurationRoot}:BaseUri overrides are limited to loopback http or any https endpoint.");
        }

        return uri.AbsoluteUri;
    }

    private static bool IsLoopbackOrHttps(Uri uri)
    {
        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && (uri.IsLoopback
                || string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal)
                || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "[::1]", StringComparison.Ordinal));
    }
}
