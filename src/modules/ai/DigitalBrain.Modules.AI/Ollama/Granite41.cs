using Microsoft.Extensions.AI;

namespace DigitalBrain.AI.Ollama;

public sealed class Granite41(
    [Llm<Granite41>] IChatClient chatClient)
    : LLM(chatClient), IGranite41;
