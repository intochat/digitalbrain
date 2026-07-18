using System.Text.Json;
using Brain.Contracts;
using Microsoft.AspNetCore.DataProtection;

namespace Brain.Kernel.Connections;

public sealed class DataProtectionConnectionTokenProtector(IDataProtectionProvider dataProtection)
    : IConnectionTokenProtector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Protect(NeuronAddress address, ConnectionToken token) =>
        Protector(address).Protect(JsonSerializer.Serialize(token, JsonOptions));

    public ConnectionToken Unprotect(NeuronAddress address, string protectedToken) =>
        JsonSerializer.Deserialize<ConnectionToken>(
            Protector(address).Unprotect(protectedToken),
            JsonOptions)
        ?? throw new InvalidOperationException("Protected connection token was empty.");

    private IDataProtector Protector(NeuronAddress address) =>
        dataProtection.CreateProtector(
            "DigitalBrain.ConnectionToken.v1",
            address.OwnerId,
            address.SpaceId,
            ProviderName(address),
            address.NeuronId);

    private static string ProviderName(NeuronAddress address)
    {
        var slash = address.NeuronId.IndexOf('/');
        var tail = slash < 0 ? address.NeuronId : address.NeuronId[(slash + 1)..];
        var dash = tail.IndexOf('-');
        return dash < 0 ? tail : tail[..dash];
    }
}
