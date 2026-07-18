namespace Ino.Core.Hosting.Llm;

/// <summary>
/// Bound from configuration section <c>Ino:Llm</c>. v0.1 only the
/// <c>bdd-mock</c> provider is implemented — real LLM wiring (azure-openai,
/// anthropic) is deferred to a post-v0.1 slice. The provider name drives
/// which <see cref="Microsoft.Extensions.AI.IChatClient"/> implementation
/// gets registered in DI by <see cref="InoLlmHostingExtensions.AddInoLlm"/>.
/// </summary>
public sealed class InoLlmOptions
{
    public const string SectionName = "Ino:Llm";
    public const string BddMock = "bdd-mock";
    public const string AzureOpenAI = "azure-openai";
    public const string Anthropic = "anthropic";

    public string Provider { get; set; } = BddMock;

    /// <summary>
    /// Extra directories BddMockChatClient scans for <c>*.feature</c> files on
    /// top of the default locations (each IDomain's assembly directory +
    /// <c>AppContext.BaseDirectory/Features</c>). Useful for test setups that
    /// stage scenarios in a temp folder.
    /// </summary>
    public IList<string> AdditionalFeaturePaths { get; set; } = new List<string>();
}
