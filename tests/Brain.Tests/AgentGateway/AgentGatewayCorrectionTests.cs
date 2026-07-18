using System.Xml.Linq;
using Brain.AgentGateway;
using Microsoft.Agents.AI;
using Xunit;

namespace Brain.Tests.AgentGateway;

public sealed class AgentGatewayCorrectionTests
{
    [Fact]
    public void AgentGateway_registers_aiagent_backed_by_typed_orleans_neuron()
    {
        var hosting = File.ReadAllText(Path.Combine(SourceRoot(), "AgentGatewayHosting.cs"));
        Assert.Contains("UseOrleansClient", hosting, StringComparison.Ordinal);
        Assert.Contains("AddAIAgent", hosting, StringComparison.Ordinal);
        Assert.Contains("GroupChatNeuronChatClient", hosting, StringComparison.Ordinal);

        var chatClient = File.ReadAllText(Path.Combine(SourceRoot(), "GroupChatNeuronChatClient.cs"));
        Assert.Contains("IClusterClient", chatClient, StringComparison.Ordinal);
        Assert.Contains("IGroupChat", chatClient, StringComparison.Ordinal);


        var program = File.ReadAllText(Path.Combine(SourceRoot(), "Program.cs"));
        Assert.Contains("AddAgentGateway", program, StringComparison.Ordinal);
        Assert.Contains("MapDevUI", program, StringComparison.Ordinal);

        Assert.True(typeof(GroupChatNeuronChatClient).IsAssignableTo(typeof(Microsoft.Extensions.AI.IChatClient)));
        Assert.Contains("IGroupChat", File.ReadAllText(Path.Combine(SourceRoot(), "GroupChatNeuronChatClient.cs")), StringComparison.Ordinal);
    }


    [Fact]
    public void AgentGateway_remains_unreferenced_by_production_projects()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        foreach (var projectPath in Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            if (projectPath.Contains("Brain.AgentGateway", StringComparison.OrdinalIgnoreCase))
                continue;
            var references = XDocument.Load(projectPath)
                .Descendants("ProjectReference")
                .Select(element => (string?)element.Attribute("Include") ?? string.Empty);
            Assert.DoesNotContain(references, reference => reference.Contains("Brain.AgentGateway", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string SourceRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Brain.AgentGateway"));
}
