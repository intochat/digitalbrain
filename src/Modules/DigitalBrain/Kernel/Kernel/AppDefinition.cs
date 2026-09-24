namespace DigitalBrain.Core;

public sealed record AppDefinition(string Id, IReadOnlyList<Type> RequiredModules, Type? Contract = null);
