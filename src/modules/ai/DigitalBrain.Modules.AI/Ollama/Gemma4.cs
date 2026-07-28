using Microsoft.Extensions.AI;

namespace DigitalBrain.AI.Ollama;

public sealed class Gemma4([Llm<Gemma4>] IChatClient chatClient) : LLM(chatClient), IGemma4;
