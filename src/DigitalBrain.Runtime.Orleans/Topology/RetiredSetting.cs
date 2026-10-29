namespace DigitalBrain.Runtime.Orleans;

/// <summary>A setting that is no longer read; its presence fails startup naming the replacement (P2-D-11).</summary>
/// <param name="Key">The retired key.</param>
/// <param name="Replacement">What to set instead.</param>
public sealed record RetiredSetting(string Key, string Replacement);
