using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace DigitalBrain.Kernel;

// CreatorNeuron: the self-evolution / creator loop piece.
// Consumes ImprovementProposal (with StructuredAction union from LLM proposals).
// Materializes actions (install bundle, run simulation as gate, emit arbitrary synapse, create .ino content via file op + install).
// Uses existing Simulation gate pattern + Outcome union for "green" decision.
// On success auto InstallBundle or emits the generated ino content as SaveFileRequest (FileOp union) then install.
// Exposes the "really intelligent" close-loop: proposal -> structured action -> gated execution -> new capability installed.
public interface ICreatorNeuron : INeuron, IHandle<ImprovementProposal>, IHandle<ApproveAction> { }

[GrainType("creator")]
public sealed class CreatorNeuron : Neuron, ICreatorNeuron
{
    private readonly IDigitalBrain? _brainOverride;

    private IDigitalBrain Brain => _brainOverride ?? GrainFactory.GetGrain<IDigitalBrain>("global");

    public CreatorNeuron(
        IDigitalBrain? brain = null)
        : base()
    {
        _brainOverride = brain;
    }

    public async Task HandleAsync(ImprovementProposal proposal, CancellationToken cancellationToken)
    {
        var action = proposal.StructuredAction;
        if (action is null)
        {
            await Emit(new NeuronTelemetry(Self, "CreatorNoStructuredAction", new Dictionary<string, string> { ["proposal"] = proposal.ProposalId }));
            return;
        }

        if (action is not null && IsPrivilegedAction(action))
        {
            // Proposal surface removed (direct); rule in os/creator.ino on: ImprovementProposal produces "Proposal: $description" card via RuleHost.
            await Emit(new NeuronTelemetry(Self, "CreatorProposalSurfacedForApprove", new Dictionary<string, string> { ["id"] = proposal.ProposalId }));
            return;
        }

        await ExecuteActionAsync(action, proposal, cancellationToken);
    }

    public async Task HandleAsync(ApproveAction approve, CancellationToken cancellationToken)
    {
        if (approve.Action is not null)
            await ExecuteActionAsync(approve.Action, null, cancellationToken);
    }

    private static bool IsPrivilegedAction(ImprovementAction? action) =>
        action is ActionInstallExperience or ActionRunSimulation;

    private async Task ExecuteActionAsync(ImprovementAction? action, ImprovementProposal? proposal, CancellationToken cancellationToken)
    {
        if (action is null) return;

        switch (action)
        {
            case ActionInstallExperience inst:
                var installed = await Brain.InstallBundleAsync(new InstallBundle((BundleId)inst.ExperienceId), cancellationToken);
                await Emit(new AgentOutcome(new OutcomeSuccess(installed)));
                await Emit(new NeuronTelemetry(Self, "CreatorInstalled", new Dictionary<string, string> { ["bundle"] = inst.ExperienceId }));
                break;

            case ActionRunSimulation sim:
                var targetDomain = sim.TargetDomainId ?? "example-world";
                await Emit(new AgentPlanStep($"creator-sim-{sim.SimulationName}", new PlanAct("run-simulation-domain", $"{sim.SimulationName}@{targetDomain}")));
                await Emit(new NeuronTelemetry(Self, "CreatorSimGateDomain", new Dictionary<string, string> { ["sim"] = sim.SimulationName, ["domain"] = targetDomain }));

                var domainBrainForGate = GrainFactory.GetGrain<IDigitalBrain>(targetDomain);
                var gateInstall = await domainBrainForGate.InstallBundleAsync(new InstallBundle((BundleId)$"sim-gate-{sim.SimulationName}"), cancellationToken);
                await Emit(new AgentOutcome(new OutcomeSuccess(gateInstall)));

                var domainJournal = await domainBrainForGate.GetFullJournalAsync(cancellationToken);
                await Emit(new NeuronTelemetry(Self, "CreatorSimGateDomainJournal", new Dictionary<string, string> { ["domain"] = targetDomain, ["journalSize"] = domainJournal.Count.ToString() }));

                await Emit(new AgentOutcome(new OutcomeSuccess(new SelfImproveRequest($"sim-gate-domain:{sim.SimulationName}@{targetDomain}"))));
                break;

            case ActionEmitSynapse emit:
                await Emit(new AgentPlanStep($"creator-emit-{emit.SynapseType}", new PlanAct("emit", emit.PayloadJson)));
                await Emit(new NeuronTelemetry(Self, "CreatorEmitted", new Dictionary<string, string> { ["type"] = emit.SynapseType }));
                break;

            case ActionCreateIno create:
                var inoContent = create.Content;
                var inoId = $"created-{create.InoPath.Replace("/", "-").Replace(".ino", "")}";
                await Emit(new SaveFileRequest(new FileOp(new FileSave(create.InoPath, inoContent, "creator generated .ino"))));

                var generatedCs = BuildRoslynGeneratedSkeleton(create.InoPath, create.Content, proposal?.Description ?? create.Content);
                var csPath = create.InoPath.Replace(".ino", ".neuron.cs");
                await Emit(new SaveFileRequest(new FileOp(new FileSave(csPath, generatedCs, "creator generated via SyntaxFactory"))));

                var createdInstall = await Brain.InstallBundleAsync(new InstallBundle((BundleId)inoId), cancellationToken);
                await Emit(new AgentOutcome(new OutcomeInstalled(inoId)));
                await Emit(new NeuronTelemetry(Self, "CreatorInoMaterialized", new Dictionary<string, string>
                {
                    ["path"] = create.InoPath,
                    ["installed"] = inoId,
                    ["roslynCs"] = csPath
                }));

                await Emit(new PackExperience((ExperienceId)inoId, "auto-packed on create", "0.1.0"));

                // Created surface removed; shell or creator.ino rule + telemetry handles post create UI.
                await Emit(new NeuronTelemetry(Self, "CreatorInoMaterializedSurfaceSuppressed", new Dictionary<string, string> { ["inoId"] = inoId }));

                break;
        }
    }

    private static string BuildRoslynGeneratedSkeleton(string inoPath, string hintContent, string desc)
    {
        var safeName = inoPath.Replace("/", "_").Replace(".ino", "").Replace("-", "_");
        var parsedName = safeName;
        string? parsedTrigger = null;
        foreach (var line in hintContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = line.Trim();
            if (t.StartsWith("name:", StringComparison.OrdinalIgnoreCase)) parsedName = t.Substring(5).Trim();
            if (t.StartsWith("triggers:", StringComparison.OrdinalIgnoreCase)) parsedTrigger = t.Substring(9).Trim();
        }
        var synapseName = $"Generated{parsedName.Replace("/", "_").Replace(".ino", "").Replace("-", "_")}Synapse";
        var neuronName = $"Generated{parsedName.Replace("/", "_").Replace(".ino", "").Replace("-", "_")}Neuron";
        var iNeuronName = $"IGenerated{parsedName.Replace("/", "_").Replace(".ino", "").Replace("-", "_")}Neuron";
        var handleType = parsedTrigger ?? synapseName;

        var usingCore = UsingDirective(ParseName("DigitalBrain.Protocol"));
        var usingProtocolEvents = UsingDirective(ParseName("DigitalBrain.Protocol.Domain.Events"));
        var usingOsEvents = UsingDirective(ParseName("DigitalBrain.Os.Domain.Events"));
        var usingApp = UsingDirective(ParseName("DigitalBrain.Os.Application"));
        var usingOrleans = UsingDirective(ParseName("Orleans"));

        var recordKeyword = Token(SyntaxKind.RecordKeyword);
        var synapseRecord = RecordDeclaration(recordKeyword, Identifier(synapseName))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.SealedKeyword))
            .AddBaseListTypes(SimpleBaseType(IdentifierName("Synapse")))
            .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
            .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));

        var handleMethod = MethodDeclaration(IdentifierName("Task"), "HandleAsync")
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.AsyncKeyword))
            .AddParameterListParameters(
                Parameter(Identifier("s")).WithType(IdentifierName(handleType)),
                Parameter(Identifier("ct")).WithType(IdentifierName("CancellationToken"))
            )
            .WithBody(Block(
                ExpressionStatement(AwaitExpression(InvocationExpression(IdentifierName("Emit"))
                    .AddArgumentListArguments(Argument(ObjectCreationExpression(IdentifierName("NeuronTelemetry"))
                        .AddArgumentListArguments(
                            Argument(IdentifierName("Self")),
                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("GeneratedHandled"))),
                            Argument(ObjectCreationExpression(IdentifierName("Dictionary<string, string>")).AddArgumentListArguments())))))),
                ExpressionStatement(AwaitExpression(InvocationExpression(IdentifierName("Emit"))
                    .AddArgumentListArguments(Argument(ObjectCreationExpression(IdentifierName("UiSurface"))
                        .AddArgumentListArguments(
                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("created-" + parsedName))),
                            Argument(IdentifierName("Self")),
                            Argument(ObjectCreationExpression(IdentifierName("Card"))
                                .AddArgumentListArguments(
                                    Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("Generated capsule active")))))))))),
                ReturnStatement()
            ));

        var neuronClass = ClassDeclaration(neuronName)
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.SealedKeyword))
            .AddBaseListTypes(
                SimpleBaseType(IdentifierName("Neuron")),
                SimpleBaseType(IdentifierName(iNeuronName))
            )
            .AddMembers(handleMethod)
            .AddAttributeLists(AttributeList(SingletonSeparatedList(Attribute(IdentifierName("GrainType")).AddArgumentListArguments(AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal("generated")))))));

        var iNeuron = InterfaceDeclaration(iNeuronName)
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddBaseListTypes(
                SimpleBaseType(IdentifierName("INeuron")),
                SimpleBaseType(GenericName(Identifier("IHandle")).AddTypeArgumentListArguments(IdentifierName(handleType)))
            );

        var ns = NamespaceDeclaration(IdentifierName("DigitalBrain.Kernel"))
            .AddMembers(iNeuron, synapseRecord, neuronClass);

        var unit = CompilationUnit()
            .AddUsings(usingCore, usingProtocolEvents, usingOsEvents, usingApp, usingOrleans)
            .AddMembers(ns);

        return unit.NormalizeWhitespace().ToFullString();
    }
}
