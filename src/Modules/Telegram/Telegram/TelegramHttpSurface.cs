using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Behaviors;
using DigitalBrain.Core;
using DigitalBrain.Time;
using DigitalBrain.UI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;

namespace DigitalBrain.Telegram;

/// <summary>Exact provider routes with their own authentication, before the general host gate.</summary>
public sealed class TelegramHttpSurface(TelegramOptions options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var metadata = new ModuleEndpointMetadata(typeof(TelegramModule));
        endpoints.MapGet("/telegram/health", HandleAsync).WithMetadata(metadata);
        endpoints.MapPost("/telegram/webhook", HandleAsync).WithMetadata(metadata);
        endpoints.MapGet("/telegram/miniapp/state", HandleAsync).WithMetadata(metadata);
        endpoints.MapPost("/telegram/miniapp/notifications/dismiss", HandleAsync).WithMetadata(metadata);
        endpoints.MapPost("/telegram/miniapp/reminders/cancel", HandleAsync).WithMetadata(metadata);
        if (options.Enabled && Directory.Exists(options.MiniAppRoot))
        {
            var root = Path.GetFullPath(options.MiniAppRoot);
            var types = new FileExtensionContentTypeProvider();
            // Register only existing files: an unknown path never inherits the module's owner-gate exemption.
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                if (relative.Split('/').Any(segment => segment.StartsWith('.')) ||
                    !types.TryGetContentType(file, out var contentType)) { continue; }
                var fullPath = Path.GetFullPath(file);
                endpoints.MapMethods("/telegram/app/" + relative, [HttpMethods.Get, HttpMethods.Head], () => Results.File(fullPath, contentType)).WithMetadata(metadata);
                if (relative == "index.html")
                {
                    endpoints.MapMethods("/telegram/app", [HttpMethods.Get, HttpMethods.Head], () => Results.File(fullPath, contentType)).WithMetadata(metadata);
                }
            }
        }
    }

    private async Task HandleAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.TrimEnd('/').ToLowerInvariant();
        context.Response.Headers.CacheControl = "no-store";
        if (!options.Enabled) { context.Response.StatusCode = 503; return; }
        if (path == "/telegram/health")
        {
            if (!HttpMethods.IsGet(context.Request.Method)) { context.Response.StatusCode = 405; return; }
            if (!TelegramAuthentication.IsWebhookSecretValid(options.WebhookSecret, context.Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString()))
            { context.Response.StatusCode = 401; return; }
            await context.Response.WriteAsJsonAsync(new
            {
                instanceId = context.RequestServices.GetRequiredService<TelegramConnectionStatus>().InstanceId,
                miniAppAvailable = File.Exists(Path.Combine(options.MiniAppRoot, "index.html")) && File.Exists(Path.Combine(options.MiniAppRoot, "main.dart.js"))
            }, context.RequestAborted).ConfigureAwait(false);
            return;
        }
        if (context.Request.ContentLength > 65536) { context.Response.StatusCode = 413; return; }
        try
        {
            if (path == "/telegram/webhook") { await WebhookAsync(context).ConfigureAwait(false); }
            else { await MiniAppAsync(context, path).ConfigureAwait(false); }
        }
        catch (JsonException) { context.Response.StatusCode = 400; }
        catch (Exception error) when (error is InvalidOperationException or TimeoutException)
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsJsonAsync(new { error = "Telegram processing is unavailable. Please retry." }, context.RequestAborted).ConfigureAwait(false);
        }
    }

    private async Task WebhookAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method)) { context.Response.StatusCode = 405; return; }
        if (!TelegramAuthentication.IsWebhookSecretValid(options.WebhookSecret, context.Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString()))
        { context.Response.StatusCode = 401; return; }
        using var document = await ReadBodyAsync(context).ConfigureAwait(false);
        if (!TelegramWebhook.TryReadMessage(document.RootElement, options.TimeZone, out var message)) { context.Response.StatusCode = 200; return; }
        var services = context.RequestServices;
        await services.GetRequiredService<ITelegramBehaviorSetup>().EnsureAsync(message!.UserId, context.RequestAborted).ConfigureAwait(false);
        var grains = services.GetRequiredService<IGrainFactory>();
        await grains.GetGrain<IBot>(new NeuronId("telegram", message.UserId.ToString(CultureInfo.InvariantCulture)).ToGrainId()).Accept(message).ConfigureAwait(false);
        services.GetService<TelegramConnectionStatus>()?.MessageReceived();
        if (message.Text.Split(' ', 2)[0] == "/start" && Uri.TryCreate(options.MiniAppUrl, UriKind.Absolute, out var url) && url.Scheme == "https")
        {
            await context.Response.WriteAsJsonAsync(new { method = "sendMessage", chat_id = message.UserId, text = "Open your reminders and inbox.",
                reply_markup = new { inline_keyboard = new[] { new[] { new { text = "Open reminders", web_app = new { url = url.AbsoluteUri } } } } } }, Json, context.RequestAborted).ConfigureAwait(false);
        }
    }

    private async Task MiniAppAsync(HttpContext context, string? path)
    {
        var now = context.RequestServices.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        if (!TelegramAuthentication.TryValidateInitData(context.Request.Headers.Authorization.ToString(), options.BotToken, now, out var userId))
        { context.Response.StatusCode = 401; return; }
        var grains = context.RequestServices.GetRequiredService<IGrainFactory>();
        var scope = "telegram-" + userId.ToString(CultureInfo.InvariantCulture);
        var notifications = grains.GetGrain<INotification>(new NeuronId("notification", scope).ToGrainId());
        var reminders = grains.GetGrain<IReminders>(new NeuronId("reminders", scope).ToGrainId());
        if (path == "/telegram/miniapp/state")
        {
            if (!HttpMethods.IsGet(context.Request.Method)) { context.Response.StatusCode = 405; return; }
            var behavior = grains.GetGrain<IBehavior>(TelegramReminderBehavior.Identity(userId).ToGrainId());
            var automation = await behavior.Read().ConfigureAwait(false);
            var diagnostics = await behavior.Diagnostics().ConfigureAwait(false);
            await context.Response.WriteAsJsonAsync(new { notifications = (await notifications.Read().ConfigureAwait(false)).Items,
                reminders = (await reminders.Read().ConfigureAwait(false)).Items,
                automation = new { status = automation.Status.ToString(), hasErrors = automation.Error is not null || diagnostics.Count > 0 } }, Json, context.RequestAborted).ConfigureAwait(false);
            return;
        }
        if (!HttpMethods.IsPost(context.Request.Method)) { context.Response.StatusCode = 405; return; }
        using var document = await ReadBodyAsync(context).ConfigureAwait(false);
        var dismiss = path == "/telegram/miniapp/notifications/dismiss";
        var property = dismiss ? "eventId" : "reminderId";
        if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Count() != 1 ||
            !document.RootElement.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()) || element.GetString()!.Length > 256)
        { context.Response.StatusCode = 400; return; }
        var identity = element.GetString()!;
        var id = TelegramAuthentication.CommandIdFor(property, userId, identity);
        if (dismiss) { await notifications.Dismiss(new DismissNotification(id, identity), context.RequestAborted).ConfigureAwait(false); }
        else { await reminders.Cancel(new CancelReminder(id, identity)).ConfigureAwait(false); }
        context.Response.StatusCode = 202;
    }

    private static async Task<JsonDocument> ReadBodyAsync(HttpContext context)
    {
        // Bound chunked requests too; Content-Length alone is not a body limit.
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int count;
        while ((count = await context.Request.Body.ReadAsync(chunk, context.RequestAborted).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > 65536) { throw new JsonException("Request body is too large."); }
            await buffer.WriteAsync(chunk.AsMemory(0, count), context.RequestAborted).ConfigureAwait(false);
        }
        return JsonDocument.Parse(buffer.ToArray());
    }
}
