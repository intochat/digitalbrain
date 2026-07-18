using Ino.Core;

namespace Ino.Core.Hosting.Llm;

public abstract class LlmModel
{
    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    public abstract string Provider { get; }

    public abstract LlmTier DefaultTier { get; }
}
