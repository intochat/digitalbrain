using DigitalBrain.InoLang.Ast;

namespace DigitalBrain.InoLang.Linking;

public sealed record LinkedPort(UsingDecl Decl, ContractSchema Schema);

public sealed record LinkedNeuron(
    NeuronDoc Doc,
    IReadOnlyDictionary<string, LinkedPort> Ports);
