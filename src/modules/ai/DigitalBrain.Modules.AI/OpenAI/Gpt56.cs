using Microsoft.Extensions.AI;

namespace DigitalBrain.AI.OpenAI;

public sealed class Gpt56(
    [Llm<Gpt56>] IChatClient chatClient)
    : LLM(chatClient), IGpt56;
