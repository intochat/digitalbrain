using DigitalBrain.Modules;

namespace DigitalBrain.Limits;

/// <summary>A promise-able action declared by a module in its <see cref="ModuleManifest"/>.</summary>
/// <param name="Name">The action identifier (<c>{module}.{verb}</c>).</param>
/// <param name="Description">The human-readable phrase describing the action in rendered policy sentences.</param>
/// <param name="AmountUnit">The unit for <see cref="PolicyMeasure.Sum"/> policies (for example <c>USD</c>), or null when only <see cref="PolicyMeasure.Count"/> is supported.</param>
public sealed record ActionDescriptor(string Name, string Description, string? AmountUnit);
