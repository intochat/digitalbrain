using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TripRadar.Server.Comms.Core.Extensions
{
    public static partial class Redactor
    {
        private static readonly Regex SensitiveKeyPattern = SensitiveKeysRegex();
        private static readonly Regex AuIdValuePattern = AuIdRegex();

        public static JsonNode? Redact(JsonNode? node) => node?.DeepClone() is { } clone ? RedactInPlace(clone) : null;

        private static JsonNode RedactInPlace(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                foreach (var key in obj.Select(item => item.Key).ToList())
                {
                    if (SensitiveKeyPattern.IsMatch(key))
                    {
                        obj[key] = "[redacted]";
                        continue;
                    }

                    if (obj[key] is not null)
                    {
                        RedactInPlace(obj[key]!);
                    }
                }
            }
            else if (node is JsonArray array)
            {
                for (var index = 0; index < array.Count; index++)
                {
                    if (array[index] is not null)
                    {
                        RedactInPlace(array[index]!);
                    }
                }
            }
            else if (node is JsonValue value && value.TryGetValue<string>(out var text))
            {
                return AuIdValuePattern.IsMatch(text) ? JsonValue.Create("[redacted]")! : node;
            }

            return node;
        }

        [GeneratedRegex("(token|cookie|authorization|auth|session|jwt|auid|uid|uuid|fingerprint|secret|password)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex SensitiveKeysRegex();

        [GeneratedRegex("^[A-Za-z0-9+/]{16,}={0,2}$", RegexOptions.CultureInvariant)]
        private static partial Regex AuIdRegex();
    }
}