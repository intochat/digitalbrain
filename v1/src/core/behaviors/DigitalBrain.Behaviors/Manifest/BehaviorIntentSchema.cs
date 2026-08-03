namespace DigitalBrain.Behaviors.Manifest;

public sealed record BehaviorIntentSchema(string SchemaId, int SchemaVersion, string RequestSchemaJson, string ResultSchemaJson);
