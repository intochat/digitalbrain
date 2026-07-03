namespace DigitalBrain.Aspire;

// Typed model marker for AddDigitalBrain's WithLLM<TModel>() — replaces raw provider/model strings
// with a compile-time-checked choice. Add a new sealed class per supported model/provider pair.
// For azureopenai models, Id is sent to Azure as the deployment name (not the base model name) —
// Azure OpenAI resolves by deployment, an arbitrary user-chosen alias (see deploy/Program.cs's
// ChatDeploymentName = "chat"). If your Azure deployment isn't literally named the model id below,
// override it after WithLLM<TModel>() via options.LlmModel = "your-deployment-name".
public abstract class LlmModel
{
    public abstract string Provider { get; }
    public abstract string Id { get; }
}

public sealed class Qwen25Coder1_5B : LlmModel
{
    public override string Provider => "ollama";
    public override string Id => "qwen2.5-coder:1.5b";
}

public sealed class Gpt4oMini : LlmModel
{
    public override string Provider => "azureopenai";
    public override string Id => "gpt-4o-mini";
}
