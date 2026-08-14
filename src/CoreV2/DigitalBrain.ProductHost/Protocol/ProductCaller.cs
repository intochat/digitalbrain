using Microsoft.AspNetCore.Http;

namespace DigitalBrain.ProductHost.Protocol;

public sealed record ProductCaller(string Workspace, string Principal)
{
    public static ProductCaller From(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var workspace = HeaderOrDefault(context, "X-DigitalBrain-Workspace", "local");
        var principal = HeaderOrDefault(context, "X-DigitalBrain-Principal", "owner");
        return new ProductCaller(workspace, principal);
    }

    private static string HeaderOrDefault(HttpContext context, string name, string fallback)
    {
        var value = context.Request.Headers[name].ToString().Trim();
        if (value.Length == 0)
        {
            return fallback;
        }

        if (value.Length > 128 || value.Any(char.IsControl))
        {
            throw new BadHttpRequestException($"Header '{name}' is invalid.");
        }

        return value;
    }
}
