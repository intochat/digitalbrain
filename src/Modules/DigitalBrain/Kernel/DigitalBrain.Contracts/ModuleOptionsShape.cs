using System.Reflection;
using System.Text.Json.Serialization;
namespace DigitalBrain.Contracts;
public static class ModuleOptionsShape
{
    public static void Validate<T>()
    {
        foreach (var property in typeof(T).GetProperties())
        {
            var name = property.Name;
            if (name.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                || new[] { "Password", "ApiKey", "PrivateKeyPem", "AccessToken", "RefreshToken" }.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            { throw new ArgumentException($"{typeof(T).Name}.{name} looks like a credential; credentials belong to the integrations registration, not module options."); }
            if (property.SetMethod is { IsPublic: true }
                && property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: not JsonIgnoreCondition.Never })
            { throw new ArgumentException($"{typeof(T).Name}.{name} is writable but excluded from module options serialization."); }
        }
    }
}
