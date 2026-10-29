using System.Text.Json;

namespace DigitalBrain.Invocation;

/// <summary>One call on a neuron contract, as it travels between a caller and a runtime: the only invocation envelope.</summary>
/// <param name="Behavior">The calling behavior, stamped by the runtime that carries the call; never by the caller.</param>
/// <param name="Contract">The contract's <see cref="Contracts.NeuronAliasAttribute"/> alias, or its full name.</param>
/// <param name="Key">The target neuron, written <c>brain/type/key</c>.</param>
/// <param name="Method">The contract method name.</param>
/// <param name="Arguments">The non-token arguments in declaration order, each encoded with <see cref="RuntimeProtocol.Json"/>.</param>
public sealed record RuntimeInvocation(string Behavior, string Contract, string Key, string Method, JsonElement[] Arguments);
