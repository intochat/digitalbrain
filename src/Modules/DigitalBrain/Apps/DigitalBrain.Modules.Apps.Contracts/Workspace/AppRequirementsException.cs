namespace DigitalBrain.Apps;

[GenerateSerializer]
public sealed class AppRequirementsException(string message) : InvalidOperationException(message)
{
    public const string MissingModules = "Cannot install this app. Missing modules: {0}.";
    public const string UnresolvableReferences = "Cannot install this app. Unresolvable contract references: {0}.";
    public const string InvalidDirective = "Cannot install this app. Invalid project directive: {0}.";
    public const string RestorePostgres = "Cannot uninstall this app. Restore the Postgres module to remove its storage.";

    public static bool IsRefusal(string message)
        => new[] { MissingModules, UnresolvableReferences, InvalidDirective }
            .Any(template => message.StartsWith(template[..template.IndexOf('{')], StringComparison.Ordinal));
}
