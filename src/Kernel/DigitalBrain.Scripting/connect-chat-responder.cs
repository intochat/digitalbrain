#:project ../Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj
#:project ../../Modules/UI/DigitalBrain.Modules.UI.Contracts/DigitalBrain.Modules.UI.Contracts.csproj
#:project ../../Modules/AI/Contracts/DigitalBrain.Modules.AI.Contracts.csproj
#:property TreatWarningsAsErrors=false

using DigitalBrain.Abstractions;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire;
using DigitalBrain.Chat;
using DigitalBrain.Client;

var positional = args.Where(static argument => !argument.StartsWith("--", StringComparison.Ordinal)).ToArray();
var chatName = positional.ElementAtOrDefault(0) ?? "main";
var modelName = positional.ElementAtOrDefault(1) ?? "default";

var brain = await DigitalBrainClient.ConnectAsync(args);
var chat = NeuronId.For<IChat>(brain.Owner, chatName);
var responder = NeuronId.For<IGemma4>(brain.Owner, modelName);

await brain.SendAsync<ISynapseGraph>(
    ISynapseGraph.InstanceName,
    new Connect(ChatRoles.ResponderConnectionId(chat), chat, ChatRoles.Responder, responder));

Console.WriteLine($"{chat} now answers through {responder}.");
