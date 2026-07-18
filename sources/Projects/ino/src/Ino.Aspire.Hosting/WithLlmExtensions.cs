using Aspire.Hosting;
using Ino.Core;
using Ino.Core.Hosting.Llm;

namespace Ino.Aspire.Hosting;

public static class WithLlmExtensions
{
    /// <summary>
    /// Provider → (Aspire parameter name, markdown description shown in the
    /// dashboard). On first launch the dashboard prompts for any unfilled
    /// secret parameter; the description renders as markdown so users get a
    /// direct link to where to obtain the key. Mirrors IAW's
    /// <c>WithLLM</c> pattern from
    /// <c>InteractiveAgents/IAW/src/Aspire.Hosting/IAWHostingExtensions.cs</c>.
    /// </summary>
    internal static readonly Dictionary<string, (string ParamName, string Description)> ProviderApiKeyMetadata =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["xai"] = (
                "xai-api-key",
                "Get your key at [console.x.ai/team/default/api-keys](https://console.x.ai/team/default/api-keys)"),
        };

    public static LlmModelSelector<TModel> WithLlm<TModel>(this IInoBuilder builder)
        where TModel : LlmModel, new()
    {
        return new LlmModelSelector<TModel>(builder, new TModel());
    }
}

public sealed class LlmModelSelector<TModel> where TModel : LlmModel
{
    readonly IInoBuilder _builder;
    readonly TModel _model;
    bool _bound;

    internal LlmModelSelector(IInoBuilder builder, TModel model)
    {
        _builder = builder;
        _model = model;
    }

    public IInoBuilder AsFast() => BindTo(LlmTier.Fast);
    public IInoBuilder AsBalanced() => BindTo(LlmTier.Balanced);
    public IInoBuilder AsReasoning() => BindTo(LlmTier.Reasoning);

    IInoBuilder BindTo(LlmTier tier)
    {
        if (_bound)
            throw new InvalidOperationException(
                $"WithLlm<{typeof(TModel).Name}> is already bound; call AsFast/AsBalanced/AsReasoning once.");
        _bound = true;
        _builder.RegisterModel(new LlmModelBinding(_model, tier));

        if (WithLlmExtensions.ProviderApiKeyMetadata.TryGetValue(_model.Provider, out var metadata))
        {
            _builder.GetOrAddApiKeyParameter(_model.Provider, app =>
                app.AddParameter(metadata.ParamName, secret: true)
                    .WithDescription(metadata.Description, enableMarkdown: true));
        }

        return _builder;
    }
}
