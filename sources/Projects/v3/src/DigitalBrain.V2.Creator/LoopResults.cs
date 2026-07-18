using DigitalBrain.V2.Ino;

namespace DigitalBrain.V2.Creator;

public sealed record Compiled(GeneratedInoCapsule Capsule, byte[] AssemblyBytes);

public sealed record CompileErrors(string[] Diagnostics);

public union CompileResult(Compiled, CompileErrors);

public sealed record Passed(string SimulationType);

public sealed record Failed(string[] Diagnostics);

public union GateOutcome(Passed, Failed);
