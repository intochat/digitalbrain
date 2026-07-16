using Brain.Client;
using Brain.Modules.Workspace;

await using var brain = await BrainCluster.Connect(args);
var chat = brain.Get<IChat>("local-owner|actor/mcp-dev|chat/main");
var reply = await chat.PostAsync(new ChatPost($"smoke {DateTimeOffset.UtcNow:O}"));
Console.WriteLine($"revision {reply.Revision}");
