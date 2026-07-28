using Microsoft.Extensions.AI;

namespace DigitalBrain.AI.Ollama;

public sealed class Llama32([Llm<Llama32>] IChatClient chatClient) : LLM(chatClient), ILlama32;
