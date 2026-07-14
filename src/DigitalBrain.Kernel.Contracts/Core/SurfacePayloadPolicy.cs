using System.Text.Json;

namespace DigitalBrain.Kernel.Contracts.Runtime;

public static class SurfacePayloadPolicy
{
    private static readonly HashSet<string> ForbiddenKeys = new(StringComparer.Ordinal)
    {
        "accesstoken", "actiontoken", "authorization", "authorizationcode", "clientid", "clientsecret",
        "actorid", "codeverifier", "grants", "ownerid", "password", "principal", "principalid", "refreshtoken",
        "secret", "secretvalue", "sessionid", "tenantid", "userid", "workspaceid"
    };

    public static void DemandSafe(JsonElement value, int depth = 0)
    {
        if (depth > 64) throw new ArgumentException("The surface payload exceeds the nesting bound.", nameof(value));
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                var normalized = new string(property.Name.Where(static character =>
                    character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9')
                    .Select(static character => char.ToLowerInvariant(character)).ToArray());
                if (ForbiddenKeys.Contains(normalized))
                    throw new ArgumentException("The surface payload contains a forbidden sensitive field.", nameof(value));
                DemandSafe(property.Value, depth + 1);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) DemandSafe(item, depth + 1);
        }
        else if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or
                 JsonValueKind.False or JsonValueKind.Null))
        {
            throw new ArgumentException("The surface payload contains an unsupported JSON value.", nameof(value));
        }
    }
}
