using System.Security.Cryptography;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Confirmation;
using DigitalBrain.Operations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace DigitalBrain.Runtime.Http.Storage;

public interface IStorageContentAuthorizer
{
    Task Authorise(BrainId brain, string storageKey, ItemId item, CancellationToken ct);
}

public sealed class Download
{
    public IResult? Error { get; init; }
    public Stream? Content { get; init; }
    public byte[]? Ranged { get; init; }
    public string ContentType { get; init; } = "application/octet-stream";
    public string FileName { get; init; } = "download";
    public bool Inline { get; init; }
    public bool Seekable { get; init; }
    public DateTimeOffset? Modified { get; init; }
    public string? ETag { get; init; }
    public long Start { get; init; }
    public long End { get; init; }
    public long? Total { get; init; }

    public static Download Fail(IResult error) => new() { Error = error };
}

public sealed class StorageContentGateway(
    Func<BrainId, string, CancellationToken, Task<IStorage>> storageResolver,
    UploadPlanBook book,
    ContentUploadLog uploads,
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection,
    TimeProvider clock,
    IStorageContentAuthorizer? authorizer = null)
{
    public const long MaxBytes = 2L * 1024 * 1024 * 1024;
    public const int CopyBuffer = 81920;

    private static readonly FileExtensionContentTypeProvider Types = new();

    private static readonly HashSet<string> InlineTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp", "image/svg+xml", "image/bmp", "image/x-icon", "image/avif",
        "audio/mpeg", "audio/wav", "audio/ogg", "audio/aac", "audio/flac", "audio/mp4", "audio/x-m4a",
        "video/mp4", "video/webm", "video/ogg", "video/quicktime", "video/x-matroska",
        "text/plain", "application/pdf"
    };

    private readonly Microsoft.AspNetCore.DataProtection.IDataProtector _tickets = protection.CreateProtector(UploadTicket.Purpose);

    public static bool SafeName(string name)
        => name.Length is > 0 and <= 255
            && !name.Contains('/', StringComparison.Ordinal)
            && !name.Contains('\\', StringComparison.Ordinal)
            && !name.Contains("..", StringComparison.Ordinal)
            && !name.Any(char.IsControl);

    public async Task<Download> Open(HttpRequest request, string brainId, string storageKey, string itemValue, CancellationToken ct)
    {
        var item = new ItemId(itemValue);
        var brain = new BrainId(brainId);
        try
        {
            if (authorizer is not null)
            {
                await authorizer.Authorise(brain, storageKey, item, ct).ConfigureAwait(false);
            }
            var storage = await storageResolver(brain, storageKey, ct).ConfigureAwait(false);
            var stat = await storage.Stat(item, ct).ConfigureAwait(false);
            if (stat.Kind != ItemKind.File) { return Download.Fail(Missing()); }
            var opened = await storage.OpenRead(item, null, ct).ConfigureAwait(false);
            var type = Media(stat.Name);
            var inline = InlineTypes.Contains(type);
            if (!opened.CanSeek && Range(request) is { } slice)
            {
                await opened.DisposeAsync().ConfigureAwait(false);
                var content = await storage.Read(item, new ByteRange(slice.Start, slice.Length), ct).ConfigureAwait(false);
                var bytes = content.Bytes.ToArray();
                var total = content.TotalLength ?? stat.Size ?? bytes.Length;
                var end = bytes.Length == 0 ? slice.Start : slice.Start + bytes.Length - 1;
                return new Download
                {
                    Ranged = bytes,
                    ContentType = type,
                    FileName = stat.Name,
                    Inline = inline,
                    Start = slice.Start,
                    End = end,
                    Total = total,
                };
            }
            return new Download
            {
                Content = opened.Content,
                ContentType = type,
                FileName = stat.Name,
                Inline = inline,
                Seekable = opened.CanSeek,
                Modified = opened.Modified ?? stat.Modified,
                ETag = opened.ETag,
            };
        }
        catch (StorageException error)
        {
            return Download.Fail(Map(error));
        }
    }

    public async Task<IResult> Put(HttpRequest request, string brainId, string storageKey, string folderValue, string name, CancellationToken ct)
    {
        if (request.ContentLength is > MaxBytes) { return TooLarge(); }
        var ticket = UploadTicket.Open(_tickets, request.Query["ticket"], clock);
        if (ticket is null || ticket.Brain != brainId || ticket.Storage != storageKey || ticket.Folder != folderValue || ticket.Name != name)
        {
            return Refused();
        }

        var folder = new ItemId(folderValue);
        var brain = new BrainId(brainId);
        try
        {
            if (authorizer is not null)
            {
                await authorizer.Authorise(brain, storageKey, folder, ct).ConfigureAwait(false);
            }
            var storage = await storageResolver(brain, storageKey, ct).ConfigureAwait(false);
            var limit = Limit((await storage.Describe(ct).ConfigureAwait(false)).Quota);
            if (request.ContentLength is { } length && length > limit) { return TooLarge(); }
            var feature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false }) { feature.MaxRequestBodySize = limit; }

            var plan = WritePlan.WriteFile(folder, name);
            if (ticket.PlanId is not null)
            {
                var parked = book.Find(ticket.PlanId);
                if (parked is null || parked.Outcome != ConfirmationOutcome.Approved
                    || parked.Brain != brainId || parked.Storage != storageKey || parked.Folder != folderValue || parked.Name != name)
                {
                    return Results.Text("Nothing was changed.", statusCode: StatusCodes.Status403Forbidden);
                }
                plan = parked.Plan;
            }

            var operation = new OperationId("op-" + Guid.NewGuid().ToString("N"));
            StorageWriteStream write;
            try
            {
                write = await storage.OpenWrite(plan, ct).ConfigureAwait(false);
            }
            catch (Conflict)
            {
                var parked = book.Park(brainId, storageKey, folder, name);
                return Results.Conflict(new Dictionary<string, string> { ["planId"] = parked.PlanId });
            }

            await using (write.ConfigureAwait(false))
            {
                try
                {
                    await request.Body.CopyToAsync(write.Content, CopyBuffer, request.HttpContext.RequestAborted).ConfigureAwait(false);
                    _ = await write.Commit(ct).ConfigureAwait(false);
                    return Results.Ok();
                }
                catch (Exception error) when (error is OperationCanceledException or IOException)
                {
                    await write.Abort(CancellationToken.None).ConfigureAwait(false);
                    uploads.Failed(operation);
                    return Results.Text("The upload was aborted.", statusCode: StatusCodes.Status400BadRequest);
                }
            }
        }
        catch (StorageException error)
        {
            return Map(error);
        }
    }

    public async Task<IResult> Ticket(string brainId, string storageKey, string folderValue, string name, string? planId, CancellationToken ct)
    {
        if (!SafeName(name)) { return Location(); }
        if (planId is { Length: > 0 })
        {
            var parked = book.Find(planId);
            if (parked is null || parked.Brain != brainId || parked.Storage != storageKey || parked.Folder != folderValue || parked.Name != name)
            {
                return Results.Text("Nothing was changed.", statusCode: StatusCodes.Status403Forbidden);
            }
            if (parked.Outcome is null) { return Results.Text("Waiting for approval.", statusCode: StatusCodes.Status409Conflict); }
            if (parked.Outcome != ConfirmationOutcome.Approved)
            {
                return Results.Text("Nothing was changed.", statusCode: StatusCodes.Status403Forbidden);
            }
        }

        var folder = new ItemId(folderValue);
        try
        {
            if (authorizer is not null)
            {
                await authorizer.Authorise(new BrainId(brainId), storageKey, folder, ct).ConfigureAwait(false);
            }
        }
        catch (StorageException error)
        {
            return Map(error);
        }

        var token = new UploadTicket(brainId, storageKey, folderValue, name, string.IsNullOrEmpty(planId) ? null : planId, clock.GetUtcNow() + UploadTicket.Lifetime).Protect(_tickets);
        var url = "/brains/" + brainId + "/storage/" + storageKey + "/items/" + Uri.EscapeDataString(folderValue)
            + "/content/" + Uri.EscapeDataString(name) + "?ticket=" + Uri.EscapeDataString(token);
        return Results.Ok(new Dictionary<string, string> { ["ticket"] = token, ["url"] = url });
    }

    private static long Limit(Quota quota)
    {
        var cap = MaxBytes;
        if (quota.State == QuotaState.Known && quota.Total is { } total) { cap = Math.Min(cap, total); }
        if (quota.State == QuotaState.Known && quota.Used is { } used && quota.Total is { } all)
        {
            cap = Math.Min(cap, Math.Max(0, all - used));
        }
        return cap;
    }

    private static string Media(string name)
        => Types.TryGetContentType(name, out var type) ? type : "application/octet-stream";

    private readonly record struct Slice(long Start, long? Length);

    private static Slice? Range(HttpRequest request)
    {
        var header = request.Headers.Range.ToString();
        if (header.Length == 0 || !RangeHeaderValue.TryParse(header, out var parsed) || parsed.Ranges.Count != 1) { return null; }
        var part = parsed.Ranges.First();
        if (part.From is not { } start) { return null; }
        long? length = part.To is { } end ? end - start + 1 : null;
        return length is < 0 ? null : new Slice(start, length);
    }

    private static IResult Map(StorageException error) => error switch
    {
        NotFound => Missing(),
        Denied => Location(),
        Unavailable => Results.Text("That storage is not on this machine.", statusCode: StatusCodes.Status503ServiceUnavailable),
        QuotaExceeded => TooLarge(),
        _ => Results.Text("That storage cannot do this.", statusCode: StatusCodes.Status400BadRequest),
    };

    private static IResult Missing() => Results.Text("That file is not here.", statusCode: StatusCodes.Status404NotFound);
    private static IResult Location() => Results.Text("That location is not available.", statusCode: StatusCodes.Status403Forbidden);
    private static IResult Refused() => Results.Text("Upload was refused.", statusCode: StatusCodes.Status403Forbidden);
    private static IResult TooLarge() => Results.Text("File is too large.", statusCode: StatusCodes.Status413PayloadTooLarge);
}
