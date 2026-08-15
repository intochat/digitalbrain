namespace Brain.Modules.Proof.Contracts;

public sealed record ProofInput(string Value);
public sealed record ProofResult(string Route);
public sealed record ProofProgress(string Phase);
public sealed record CorrectionInput(string RequestedRoute);
public sealed record CorrectionResult(string AppliedRoute);
public sealed record ProofCapabilityInput(string Value);
public sealed record ProofCapabilityResult(string Route);
