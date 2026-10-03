using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Aspire.Hosting;

public sealed record BrainModuleAnnotation(string ModuleId) : IResourceAnnotation;
