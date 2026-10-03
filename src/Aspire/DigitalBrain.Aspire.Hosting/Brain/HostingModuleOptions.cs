using System.Text.Json;
using Microsoft.Extensions.Configuration;
using DigitalBrain.Contracts;
namespace DigitalBrain.Aspire.Hosting;

public static class HostingModuleOptions
{
    public static T GetModuleOptions<T>(this IConfiguration configuration, string id) where T : class, IModuleOptions, new()
    {
        var value = configuration.GetSection($"DigitalBrain:Modules:{id}:Options").Get<T>() ?? new();
        value.Validate();
        return value;
    }
    internal static Dictionary<string, string?> Flatten<T>(T options, string id)
    {
        ModuleOptionsShape.Validate<T>();
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Walk(JsonSerializer.SerializeToElement(options), $"DigitalBrain:Modules:{id}:Options");
        return values;
        void Walk(JsonElement element, string path)
        {
            if (element.ValueKind == JsonValueKind.Object)
            { foreach (var property in element.EnumerateObject()) { Walk(property.Value, path + ":" + property.Name); } }
            else if (element.ValueKind == JsonValueKind.Array)
            { var index = 0; foreach (var value in element.EnumerateArray()) { Walk(value, path + ":" + index++); } }
            else { values[path] = element.ValueKind == JsonValueKind.Null ? null : element.ToString(); }
        }
    }
}
