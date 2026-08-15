using Microsoft.Extensions.AI;

namespace DigitalBrain.AI.Ollama;

public sealed class Qwen35([Llm<Qwen35>] IChatClient chatClient) : LLM(chatClient), IQwen35;
