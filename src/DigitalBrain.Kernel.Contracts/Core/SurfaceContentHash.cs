using System.Security.Cryptography;
using System.Text.Json;

namespace DigitalBrain.Kernel.Contracts.Runtime;

public static class SurfaceContentHash
{
    public static string Compute(JsonElement payload, IReadOnlyList<StoredActionBinding> actions)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            payload,
            actions = actions.Select(static action => new
            {
                action.BindingId,
                action.ActionType,
                action.InputSchemaRef,
                action.RequiredGrant,
                action.MaxUses,
                action.ExpiresAt,
                action.ActionSchemaVersion
            })
        });
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
