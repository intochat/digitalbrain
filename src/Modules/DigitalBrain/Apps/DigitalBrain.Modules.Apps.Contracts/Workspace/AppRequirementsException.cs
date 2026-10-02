namespace DigitalBrain.Apps;

[GenerateSerializer]
public sealed class AppRequirementsException(string message) : InvalidOperationException(message)
{
    public const string MissingModules = "Cannot install this app. Missing modules: {0}.";
    public const string UnresolvableReferences = "Cannot install this app. Unresolvable contract references: {0}.";
    public const string InvalidDirective = "Cannot install this app. Invalid project directive: {0}.";
}
