using DigitalBrain.Core.Models;

namespace DigitalBrain.Aspire;

public sealed class DigitalBrainOptions
{
    private int? lastModelRegistration;
    private string? llmModel;
    private string llmProvider = DigitalBrainProviderIds.Ollama;
    private bool llmModelOverridden;
    private bool llmProviderOverridden;

    public DigitalBrainModelRegistry ModelRegistry { get; } = new();

    public string? LlmModel
    {
        get => llmModel;
        set
        {
            llmModel = value;
            llmModelOverridden = true;
        }
    }

    public string LlmProvider
    {
        get => llmProvider;
        set
        {
            llmProvider = value;
            llmProviderOverridden = true;
        }
    }

    public string ResolvedLlmProvider =>
        !llmProviderOverridden && ModelRegistry.DefaultLlm is { } defaultLlm
            ? defaultLlm.Model.Provider
            : llmProvider;

    public string? ResolvedLlmModel =>
        !llmModelOverridden && ModelRegistry.DefaultLlm is { } defaultLlm
            ? defaultLlm.Model.Id
            : llmModel;

    public int KernelReplicas { get; set; } = 3;

    public bool EnableMcp { get; set; } = true;

    public DigitalBrainOptions WithLLM<TModel>() where TModel : LlmModel, new()
    {
        var model = new TModel();
        lastModelRegistration = ModelRegistry.Register(model.Describe(), DigitalBrainModelRole.Balanced);
        SelectLlm(model);
        return this;
    }

    public DigitalBrainOptions WithEmbedding<TModel>() where TModel : EmbeddingModel, new()
    {
        var model = new TModel();
        lastModelRegistration = ModelRegistry.Register(model.Describe(), DigitalBrainModelRole.Default);
        return this;
    }

    public DigitalBrainOptions AsFast() => SetLastModelRole(DigitalBrainModelRole.Fast);

    public DigitalBrainOptions AsBalanced() => SetLastModelRole(DigitalBrainModelRole.Balanced);

    public DigitalBrainOptions AsReasoning() => SetLastModelRole(DigitalBrainModelRole.Reasoning);

    private DigitalBrainOptions SetLastModelRole(DigitalBrainModelRole role)
    {
        if (lastModelRegistration is null)
        {
            throw new InvalidOperationException("Register a model before assigning a routing role.");
        }

        ModelRegistry.SetRole(lastModelRegistration.Value, role);
        return this;
    }

    private void SelectLlm(DigitalBrainModel model)
    {
        llmProvider = model.Provider;
        llmModel = model.Id;
        llmProviderOverridden = false;
        llmModelOverridden = false;
    }
}
