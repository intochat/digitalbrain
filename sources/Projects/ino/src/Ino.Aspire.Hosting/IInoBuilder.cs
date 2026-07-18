using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;

namespace Ino.Aspire.Hosting;

public interface IInoBuilder
{
    IDistributedApplicationBuilder AppBuilder { get; }

    /// <summary>
    /// The IAW substrate that AddIno booted under the hood — Orleans cluster,
    /// blob storage, Qdrant, optional Ollama. Pass to silo resources via
    /// <c>silo.WithReference(ino.Iaw)</c> to inherit Orleans clustering, the
    /// LLM environment block, blob/Qdrant connections, and WaitFor wiring.
    /// </summary>
    IAWService Iaw { get; }

    IReadOnlyList<IDomain> RegisteredDomains { get; }
    void RegisterDomain(IDomain domain);

    IReadOnlyList<LlmModelBinding> DeclaredModels { get; }
    void RegisterModel(LlmModelBinding binding);

    VoiceToTextProvider? DeclaredVoiceProvider { get; }
    void RegisterVoiceProvider(VoiceToTextProvider provider);

    /// <summary>
    /// API-key parameter resources keyed by provider name (e.g. "xai" → the
    /// secret <see cref="ParameterResource"/> the Aspire dashboard prompts
    /// for on first run).
    /// </summary>
    IReadOnlyDictionary<string, IResourceBuilder<ParameterResource>> ApiKeyParameters { get; }

    /// <summary>
    /// Registers (or returns the existing) API-key parameter for a provider.
    /// Idempotent — calling twice for the same provider returns the same
    /// <see cref="ParameterResource"/> so the dashboard prompts once even
    /// when multiple models from one provider are declared.
    /// </summary>
    IResourceBuilder<ParameterResource> GetOrAddApiKeyParameter(
        string provider,
        Func<IDistributedApplicationBuilder, IResourceBuilder<ParameterResource>> factory);
}
