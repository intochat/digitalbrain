using Ino.Core;
using Microsoft.Extensions.AI;

namespace Ino.Core.Hosting.Llm;

public interface IChatClientFactory
{
    IChatClient ForTier(LlmTier tier);
    IReadOnlyList<LlmModel> RegisteredModels { get; }
}
