using System;
using Brain.Client;
using Brain.Modules.Workspace;
using Brain.Modules.Web;

await using var brain = await BrainCluster.Connect(args);

var chat = brain.Get<IChat>("local-owner|actor/dev|chat/main");
var posted = await chat.PostAsync(new ChatPost("inbox-brief behavior ran under its own identity"));
Console.WriteLine($"granted chat.post -> revision {posted.Revision}");

try
{
    var web = brain.Get<IWeb>("local-owner|actor/dev|web/probe");
    await web.FetchAsync(new WebFetch("https://example.com/"));
    Console.WriteLine("UNEXPECTED: ungranted web.fetch succeeded");
}
catch (Exception ex)
{
    Console.WriteLine($"ungranted web.fetch refused -> {ex.Message}");
}
