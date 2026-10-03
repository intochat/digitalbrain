using DigitalBrain.Sdk.Types;
using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.Sdk.Http;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Card;
using DigitalBrain.Flutter.Collection;
using DigitalBrain.Flutter.Form;
using DigitalBrain.Flutter.ImageCanvas;
using DigitalBrain.Flutter.Layout;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.Tabs;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Flutter.VoiceInput;
using DigitalBrain.Flutter.Select;
using DigitalBrain.Flutter.FileInput;
using DigitalBrain.Flutter.Workspace;
using DigitalBrain.Flutter.WebBrowser;
using DigitalBrain.Apps;
using DigitalBrain.Kernel.Enforcement;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Flutter;

internal static class AppUiEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var apps = BrainRoutes.Group(endpoints, "/apps");
        apps.MapGet("/node", (string kind, string name, IDigitalBrain brain, CancellationToken ct) => ModuleHttp.Respond(async () =>
        {
            var scope = BrainScope.CurrentId();
            if (!OwnsUi(scope, name)) { throw new UnauthorizedAccessException("The UI belongs to another brain."); }
            return kind switch
            {
                "text" => Results.Ok(await brain.Get<IText>(name).Read().WaitAsync(ct)),
                "button" => Results.Ok(await brain.Get<IButton>(name).Read().WaitAsync(ct)),
                "textfield" => Results.Ok(await brain.Get<ITextField>(name).Read().WaitAsync(ct)),
                "select" => Results.Ok(await brain.Get<ISelect>(name).Read().WaitAsync(ct)),
                "fileinput" => Results.Ok(await brain.Get<IFileInput>(name).Read().WaitAsync(ct)),
                "card" => Results.Ok(await brain.Get<ICard>(name).Read().WaitAsync(ct)),
                "tabs" => Results.Ok(await brain.Get<ITabs>(name).Read().WaitAsync(ct)),
                "surface" => Results.Ok(await brain.Get<ISurface>(name).Read().WaitAsync(ct)),
                "layout" => Results.Ok(await brain.Get<ILayout>(name).Read().WaitAsync(ct)),
                "collection" => Results.Ok(await brain.Get<ICollectionView>(name).Read().WaitAsync(ct)),
                "imagecanvas" => Results.Ok(await brain.Get<IImageCanvas>(name).Read().WaitAsync(ct)),
                "form" => Results.Ok(await brain.Get<IForm>(name).Read().WaitAsync(ct)),
                UIVocabulary.VoiceInputType => Results.Ok(await brain.Get<IVoiceInput>(name).Read().WaitAsync(ct)),
                UIVocabulary.WebBrowserType => Results.Ok(await brain.Get<IWebBrowser>(name).Read().WaitAsync(ct)),
                _ => throw new ArgumentException("Unknown app UI kind.")
            };
        }));
        apps.MapPost("/event", (UiEvent input, IDigitalBrain brain, CancellationToken ct) => ModuleHttp.Respond(async () =>
        {
            var scope = BrainScope.CurrentId();
            if (!OwnsUi(scope, input.Name)) { throw new UnauthorizedAccessException(); }
            switch (input.Kind)
            {
                case UIVocabulary.WebBrowserType:
                    BrowserConnection connection;
                    try { connection = JsonSerializer.Deserialize<BrowserConnection>(input.Value ?? "", JsonSerializerOptions.Web) ?? throw new ArgumentException("Browser connection is required."); }
                    catch (JsonException) { throw new ArgumentException("Invalid browser connection."); }
                    var browser = brain.Get<IWebBrowserConnector>(input.Name);
                    if (input.Action == "connect") { await browser.Connect(connection.Port, connection.SessionId).WaitAsync(ct); }
                    else if (input.Action == "disconnect") { await browser.Disconnect(connection.SessionId).WaitAsync(ct); }
                    else { throw new ArgumentException("Unknown browser action."); }
                    break;
                case "button": await brain.Get<IButton>(input.Name).Click().WaitAsync(ct); break;
                case "textfield": await brain.Get<ITextField>(input.Name).Input(input.Value ?? "").WaitAsync(ct); break;
                case "select": await brain.Get<ISelect>(input.Name).Choose(input.Value ?? "").WaitAsync(ct); break;
                case "fileinput": await brain.Get<IFileInput>(input.Name).Capture(input.Field ?? "", input.Value ?? "").WaitAsync(ct); break;
                case "tabs": await brain.Get<ITabs>(input.Name).Select(input.Value ?? "").WaitAsync(ct); break;
                case "collection":
                    var collection = brain.Get<ICollectionView>(input.Name);
                    if (input.Action == "select") { await collection.Select(input.Value ?? "", input.Revision).WaitAsync(ct); }
                    else { await collection.Activate(input.Value ?? "", input.Revision).WaitAsync(ct); }
                    break;
                case "form":
                    var form = brain.Get<IForm>(input.Name);
                    if (input.Action == "submit")
                    {
                        var state = await form.Read().WaitAsync(ct);
                        var values = state.Fields.Select(field => new FormFieldValue(field.Name, field.Value)).ToArray();
                        await form.Submit(new(values, (int)input.Revision)).WaitAsync(ct);
                    }
                    else if (input.Action == "secret")
                    {
                        if (!SecretReferences.IsReference(input.Value))
                        {
                            throw new ArgumentException("A form secret carries the vault reference, never a raw secret value.");
                        }

                        await form.SetSecret(input.Field ?? "", SecretReferences.FromReference(input.Value!, input.Field ?? "")).WaitAsync(ct);
                    }
                    else { await form.SetDraft(input.Field ?? "", input.Value ?? "").WaitAsync(ct); }
                    break;
                case UIVocabulary.VoiceInputType:
                    await brain.Get<IVoiceInput>(input.Name).Capture(DecodeAudio(input.Value), input.MimeType ?? "").WaitAsync(ct);
                    break;
                default: throw new ArgumentException("Unknown UI event.");
            }
            return Results.Ok(new { delivered = true });
        }));
    }

    private static bool OwnsUi(string scope, string name) =>
        new[] { "/apps/", "/images/", "/applications/", "/packages/" }.Any(area => name.StartsWith(scope + area, StringComparison.Ordinal));

    private static byte[] DecodeAudio(string? base64)
    {
        try { return Convert.FromBase64String(base64 ?? ""); }
        catch (FormatException) { throw new ArgumentException("Audio must be base64 encoded."); }
    }

    internal sealed record UiEvent(string Kind, string Name, string? Action = null, string? Value = null, long Revision = 0, string? Field = null, string? MimeType = null);
    private sealed record BrowserConnection(int Port, string SessionId);
}
