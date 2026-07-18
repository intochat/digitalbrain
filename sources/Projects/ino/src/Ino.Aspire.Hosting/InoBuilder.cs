using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;

namespace Ino.Aspire.Hosting;

internal sealed class InoBuilder(IDistributedApplicationBuilder appBuilder, IAWService iaw) : IInoBuilder
{
    private readonly List<IDomain> _domains = [];
    private readonly List<LlmModelBinding> _models = [];
    private readonly Dictionary<string, IResourceBuilder<ParameterResource>> _apiKeys =
        new(StringComparer.OrdinalIgnoreCase);
    private VoiceToTextProvider? _voice;

    public IDistributedApplicationBuilder AppBuilder { get; } = appBuilder;

    public IAWService Iaw { get; } = iaw;

    public IReadOnlyList<IDomain> RegisteredDomains => _domains;
    public void RegisterDomain(IDomain domain) => _domains.Add(domain);

    public IReadOnlyList<LlmModelBinding> DeclaredModels => _models;
    public void RegisterModel(LlmModelBinding binding) => _models.Add(binding);

    public VoiceToTextProvider? DeclaredVoiceProvider => _voice;
    public void RegisterVoiceProvider(VoiceToTextProvider provider) => _voice = provider;

    public IReadOnlyDictionary<string, IResourceBuilder<ParameterResource>> ApiKeyParameters => _apiKeys;

    public IResourceBuilder<ParameterResource> GetOrAddApiKeyParameter(
        string provider,
        Func<IDistributedApplicationBuilder, IResourceBuilder<ParameterResource>> factory)
    {
        if (_apiKeys.TryGetValue(provider, out var existing))
            return existing;

        var created = factory(AppBuilder);
        _apiKeys[provider] = created;
        return created;
    }
}
