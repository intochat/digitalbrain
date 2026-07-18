namespace DigitalBrain.Kernel;

[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class ConversationStateAttribute : Attribute, IFacetMetadata;
