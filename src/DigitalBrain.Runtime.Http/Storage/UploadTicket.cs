using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace DigitalBrain.Runtime.Http.Storage;

/// <summary>
/// HMAC ticket (Data Protection) bound to a brain, storage, folder, name and optional overwrite <see cref="PlanId"/>.
/// Lives for ten minutes. A ticket without <see cref="PlanId"/> cannot overwrite.
/// </summary>
public sealed record UploadTicket(string Brain, string Storage, string Folder, string Name, string? PlanId, DateTimeOffset Expires)
{
    public const string Purpose = "DigitalBrain.Runtime.Http.Storage.UploadTicket";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public string Protect(IDataProtector protector)
    {
        ArgumentNullException.ThrowIfNull(protector);
        return protector.Protect(JsonSerializer.Serialize(this));
    }

    public static UploadTicket? Open(IDataProtector protector, string? token, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(clock);
        if (string.IsNullOrEmpty(token)) { return null; }
        try
        {
            var ticket = JsonSerializer.Deserialize<UploadTicket>(protector.Unprotect(token));
            return ticket is { Brain.Length: > 0 } && ticket.Expires > clock.GetUtcNow() ? ticket : null;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}
