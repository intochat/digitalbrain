using System.Xml.Linq;
using Brain.Contracts;
using Google.Contracts;
using Xunit;

namespace DigitalBrain.Tests.Google;

public class GoogleContractTests
{
    [Fact]
    public void Contract_project_depends_only_on_brain_contracts()
    {
        var project = XDocument.Load(Path.Combine(
            RepositoryRoot(),
            "modules",
            "Google.Contracts",
            "Google.Contracts.csproj"));
        var references = project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value.Replace('\\', '/'))
            .ToArray();

        Assert.Equal(["../../kernel/Brain.Contracts/Brain.Contracts.csproj"], references);
        Assert.Empty(project.Descendants("PackageReference"));
    }

    [Fact]
    public void Capability_ids_are_stable()
    {
        Assert.Equal("google.gmail.message.read.v1", GoogleCapabilityIds.GmailMessageRead);
        Assert.Equal("google.gmail.mailbox.read.v1", GoogleCapabilityIds.GmailMailboxRead);
        Assert.Equal("google.gmail.send.propose.v1", GoogleCapabilityIds.GmailSendPropose);
        Assert.Equal("google.gmail.send.execute.v1", GoogleCapabilityIds.GmailSendExecute);
        Assert.Equal("google.gmail.inbox.summarize.v1", GoogleCapabilityIds.GmailInboxSummarize);
    }

    [Fact]
    public void Gmail_contract_limits_are_enforced()
    {
        _ = new GmailMailboxReadRequest(1);
        _ = new GmailMailboxReadRequest(100);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GmailMailboxReadRequest(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GmailMailboxReadRequest(101));
        Assert.Throws<ArgumentException>(() => new GmailMessageReadRequest(new string('m', 513)));
        Assert.Throws<ArgumentException>(() => new GmailSendProposalRequest(
            new string('a', 321),
            "subject",
            "body",
            "operation"));
        Assert.Throws<ArgumentException>(() => new GmailSendProposalRequest(
            "a@example.com",
            new string('s', 999),
            "body",
            "operation"));
        Assert.Throws<ArgumentException>(() => new GmailSendProposalRequest(
            "a@example.com",
            "subject",
            new string('b', 100_001),
            "operation"));
        Assert.Throws<ArgumentException>(() => new GmailMessage(
            "message",
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            new string('b', 1_000_001)));
    }

    [Fact]
    public void Gmail_neuron_exposes_the_typed_contract_boundary()
    {
        var methods = typeof(IGmailNeuron).GetMethods().ToDictionary(method => method.Name);

        AssertContract<GmailMailboxPage>(methods[nameof(IGmailNeuron.ReadMailboxAsync)], GoogleCapabilityIds.GmailMailboxRead);
        AssertContract<GmailMessage>(methods[nameof(IGmailNeuron.ReadMessageAsync)], GoogleCapabilityIds.GmailMessageRead);
        AssertContract<NeuronReply<GmailSendProposal>>(
            methods[nameof(IGmailNeuron.ProposeSendAsync)],
            GoogleCapabilityIds.GmailSendPropose);
        AssertContract<GmailSendResult>(
            methods[nameof(IGmailNeuron.ExecuteSendAsync)],
            GoogleCapabilityIds.GmailSendExecute);
    }

    private static void AssertContract<TResult>(System.Reflection.MethodInfo method, string capabilityId)
    {
        Assert.Equal(typeof(Task<TResult>), method.ReturnType);
        Assert.Equal(capabilityId, method.GetCustomAttributes(typeof(NeuronContractAttribute), false)
            .Cast<NeuronContractAttribute>()
            .Single()
            .Contract);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
