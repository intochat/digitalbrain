namespace DigitalBrain.Behaviors.Manifest;

public sealed record BehaviorEntryPoints(
    IReadOnlyList<string> EventAliases,
    IReadOnlyList<BehaviorIntentSchema> IntentSchemas);

public sealed record BehaviorIntentSchema(
    string SchemaId,
    int SchemaVersion,
    string RequestSchemaJson,
    string ResultSchemaJson);
