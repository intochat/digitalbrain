using Ino.Core.Hosting.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace Ino.Llm.Xai;

/// <summary>
/// xAI provider — wraps the OpenAI SDK against the
/// <c>https://api.x.ai/v1</c> endpoint. Stateless; reads the API key from
/// <c>Ino:Llm:ApiKeys:xai</c> on every <see cref="CreateClient"/> call so
/// the silo picks up the value the moment the Aspire dashboard's
/// <c>xai-api-key</c> parameter is filled in. Discovered automatically by
/// <see cref="AddInoChatClientsExtensions.AddInoChatClients"/> via assembly
/// scan of declared model types.
/// </summary>
public sealed class XaiProviderFactory : ILlmProviderFactory
{
    static readonly Uri XaiEndpoint = new("https://api.x.ai/v1");

    public string Provider => "xai";

    public bool IsConfigured(IConfiguration config)
        => !string.IsNullOrWhiteSpace(config[LlmConfig.XaiApiKey]);

    public IChatClient CreateClient(LlmModel model, IConfiguration config, HttpClient? httpClient = null)
    {
        var apiKey = config[LlmConfig.XaiApiKey];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                $"'{LlmConfig.XaiApiKey}' not configured. Aspire prompts for the " +
                "'xai-api-key' parameter in the dashboard on first run; enter it " +
                "there and the silo will pick it up.");

        var options = new OpenAIClientOptions { Endpoint = XaiEndpoint };
        if (httpClient is not null)
            options.Transport = new HttpClientPipelineTransport(httpClient);

        return new OpenAIClient(new ApiKeyCredential(apiKey), options)
            .GetChatClient(model.Id)
            .AsIChatClient();
    }
}
