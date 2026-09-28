namespace DigitalBrain.AI.Agents;

[GenerateSerializer, Alias("db.ai.agent-reply")]
public sealed record AgentReply([property: Id(0)] string Text);