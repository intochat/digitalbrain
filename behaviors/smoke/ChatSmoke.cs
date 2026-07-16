using Brain.Client;
using Brain.Modules.Workspace;
using Brain.Modules.Ai;

await using var brain = await BrainCluster.Connect(args);
var chat = brain.Get<IChat>("local-owner|actor/mcp-dev|chat/main");
var reply = await chat.PostAsync(new ChatPost($"smoke {DateTimeOffset.UtcNow:O}"));
Console.WriteLine($"revision {reply.Revision}");

var llm = brain.Get<ILlm>("local-owner|actor/mcp-dev|llm/balanced");
try
{
    var completion = await llm.CompleteAsync(new LlmRequest("Reply with exactly: SCRIPT-OK"));
    Console.WriteLine($"llm[{completion.Model}] {completion.Text}");
}
catch (Exception exception)
{
    Console.WriteLine($"llm skipped: {exception.Message}");
}
