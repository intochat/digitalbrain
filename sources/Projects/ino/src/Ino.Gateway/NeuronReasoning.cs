namespace Ino.Gateway;

/// <summary>
/// Provenance record for the inspector drawer's Reasoning panel. For the v0.1
/// BDD-driven demo, <see cref="Source"/> is typically <c>bdd-mock</c> and
/// <see cref="ScenarioName"/> points at the .feature scenario that produced the
/// response. Real LLM runs populate <see cref="Source"/> with the provider name
/// and <see cref="Text"/> with prompt/response excerpts.
/// </summary>
public sealed record NeuronReasoning(
    string NeuronId,
    string Source,
    string ScenarioName,
    string Text);
