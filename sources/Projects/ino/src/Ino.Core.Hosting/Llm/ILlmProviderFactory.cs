using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace Ino.Core.Hosting.Llm;

/// <summary>
/// One implementation per LLM provider (xai, openai, anthropic, ollama, …).
/// Stateless: the factory reads its API key and any other connection state
/// from <see cref="IConfiguration"/> on each call. The aggregating
/// <see cref="ProviderBackedChatClientFactory"/> handles client caching and
/// tier resolution on top.
///
/// <para>Convention: each <c>Ino.Llm.&lt;Provider&gt;</c> assembly contains
/// exactly one type implementing this interface; the silo discovers it by
/// scanning the assembly that contains the <see cref="LlmModel"/> declared
/// in the AppHost. Add a new provider = one factory class + one model class
/// per tier + one <c>WithLlm&lt;TModel&gt;()</c> line in <c>Program.cs</c>.</para>
/// </summary>
public interface ILlmProviderFactory
{
    /// <summary>
    /// Provider key — must match <see cref="LlmModel.Provider"/> on the
    /// models this factory builds clients for. Compared
    /// case-insensitively.
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Returns true when the provider has all the configuration it needs
    /// to build a client (typically: an API key is set under the prefix
    /// declared in <see cref="LlmConfig"/>). The aggregating tier-resolver
    /// surfaces a clear error if a declared model's provider is not
    /// configured, instead of failing deep inside the SDK.
    /// </summary>
    bool IsConfigured(IConfiguration config);

    /// <summary>
    /// Builds the underlying <see cref="IChatClient"/> for one model.
    /// <paramref name="httpClient"/>, when supplied, is the resilience-
    /// equipped per-provider <c>HttpClient</c> from
    /// <see cref="LlmResilienceConfiguration"/>; the factory routes
    /// requests through it so retry/circuit breaker/timeout policies
    /// apply uniformly across providers.
    /// </summary>
    IChatClient CreateClient(LlmModel model, IConfiguration config, HttpClient? httpClient = null);
}
