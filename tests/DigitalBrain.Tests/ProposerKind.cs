using System.Security.Cryptography;
using System.Text;
using Brain.Contracts;

namespace Brain.KernelTests;

public sealed class ProposerKind : INeuronKind
{
    public string Kind => "proposer";
    public string[] Contracts => ["proposer.send.v1"];

    public ValueTask<KindResult> InvokeAsync(NeuronContext context, NeuronInvocation invocation) =>
        invocation.Contract switch
        {
            "proposer.send.v1" => ValueTask.FromResult(new KindResult(
                invocation.InputJson,
                [("send-proposed", invocation.InputJson)],
                new EffectProposal("test-provider", invocation.InputJson, Sha256Hex(invocation.InputJson)))),
            _ => throw new BrainException(BrainErrors.UnknownContract, invocation.Contract)
        };

    public string Project(NeuronContext context, string projection) =>
        $$"""{"eventCount":{{context.Journal.Count}}}""";

    private static string Sha256Hex(string input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}
