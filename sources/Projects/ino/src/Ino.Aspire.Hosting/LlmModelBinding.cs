using Ino.Core;
using Ino.Core.Hosting.Llm;

namespace Ino.Aspire.Hosting;

public sealed record LlmModelBinding(LlmModel Model, LlmTier Tier);
