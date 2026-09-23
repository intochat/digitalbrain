using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI.Agents;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Form;
using DigitalBrain.Flutter.Workspace;
using IntoChat.Apps;
using Microsoft.Extensions.AI;

namespace IntoChat.Agent;

// One declarative form window per assistant call. The model asks for typed fields by name and
// receives the handle plus the field types; submitted values never travel back to the model.
internal sealed class WorkspaceFormTools(IDigitalBrain brain, AppSurfaceComposer surfaces) : IAgentToolFactory
{
    private static readonly Dictionary<string, FieldKind> Kinds = BuildKinds();

    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        async Task<object> ShowForm(
            [Description("Short window and form title.")] string title,
            [Description("Fields to collect, in order; each kind is a semantic type id.")] IReadOnlyList<FormFieldArgument> fields,
            CancellationToken ct)
        {
            var trusted = context();
            try { return await OpenAsync(trusted, title, fields, ct); }
            catch (Exception error) when (error is ArgumentException or TypeValidationException or InvalidOperationException or KeyNotFoundException)
            { return Failure(error); }
        }

        async Task<object> ShowView(
            [Description("The form handle returned by show_form.")] string formId,
            CancellationToken ct)
        {
            var trusted = context();
            try { return await ViewAsync(trusted, formId, ct); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException)
            { return Failure(error); }
        }

        return [
            AIFunctionFactory.Create(ShowForm, "show_form", "Open an interactive typed form window in the user's workspace. The result is a handle and the field types; it never contains values the user typed. If isError=true, repair the fields and retry."),
            AIFunctionFactory.Create(ShowView, "show_view", "Reopen a form window by its handle and return the field types and revision only, never values. If isError=true, repair the argument and retry."),
        ];
    }

    private async Task<object> OpenAsync(AgentToolContext trusted, string title, IReadOnlyList<FormFieldArgument> fields, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) { throw new ArgumentException("Provide a title with 1–200 characters.", nameof(title)); }
        if (fields is null || fields.Count is 0 or > 64) { throw new ArgumentException("A form declares 1–64 fields.", nameof(fields)); }
        var declared = new List<FormField>(fields.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name) || field.Name.Length > 64 || !seen.Add(field.Name))
            { throw new ArgumentException($"Field name '{field.Name}' must be unique and 1–64 characters.", nameof(fields)); }
            declared.Add(new(field.Name, string.IsNullOrWhiteSpace(field.Label) ? field.Name : field.Label, ParseKind(field.Kind), field.Required, field.Choices));
        }
        var formId = trusted.ScopeId + "/apps/forms/" + Hash(JsonSerializer.Serialize(new[] { trusted.ScopeId, trusted.RunId, trusted.CallId }));
        var state = await brain.Get<IForm>(formId).Define(new(title, declared)).WaitAsync(ct);
        await EnsureWindowAsync(trusted.ScopeId, formId, title, await surfaces.Form(formId, title, formId), ct);
        return Handle(state, formId);
    }

    private async Task<object> ViewAsync(AgentToolContext trusted, string formId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(formId) || formId.Length > 256 || !formId.StartsWith(trusted.ScopeId + "/apps/", StringComparison.Ordinal))
        { throw new ArgumentException("The form handle belongs to another workspace.", nameof(formId)); }
        var state = await brain.Get<IForm>(formId).Read().WaitAsync(ct);
        if (state.Fields.Count == 0) { throw new KeyNotFoundException("No such form; call show_form first."); }
        await EnsureWindowAsync(trusted.ScopeId, formId, state.Title, await surfaces.Form(formId, state.Title, formId), ct);
        return Handle(state, formId);
    }

    private async Task EnsureWindowAsync(string scope, string windowId, string title, UiChildRef surface, CancellationToken ct)
    {
        var workspace = brain.Get<IWorkspace>(scope);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var state = await workspace.Read().WaitAsync(ct);
            if (state.Windows.Any(window => window.Id == windowId && window.IsOpen && window.Surface == surface)) { return; }
            try { await workspace.OpenSurface(new(Guid.NewGuid().ToString(), windowId, title, surface, state.Revision)).WaitAsync(ct); return; }
            catch (WorkspaceRevisionConflictException) when (attempt < 3) { }
        }
        throw new InvalidOperationException("The workspace changed too often; retry this operation.");
    }

    private static FieldKind ParseKind(string kind)
    {
        if (!string.IsNullOrWhiteSpace(kind) && Kinds.TryGetValue(kind.Trim(), out var parsed)) { return parsed; }
        throw new ArgumentException($"'{kind}' is not a semantic type. Allowed kinds: {string.Join(", ", Kinds.Keys)}.", nameof(kind));
    }

    private static Dictionary<string, FieldKind> BuildKinds()
    {
        var kinds = new Dictionary<string, FieldKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in TypeCatalog.All)
        {
            kinds[type.Id] = type.Kind;
            kinds[type.Kind.ToString()] = type.Kind;
        }
        return kinds;
    }

    private static object Handle(FormState state, string formId) => new
    {
        windowId = formId,
        formId,
        title = state.Title,
        revision = state.Revision,
        submitted = state.Submitted,
        fields = state.Fields.Select(field =>
        {
            var type = TypeCatalog.Get(field.Kind);
            return new
            {
                name = field.Name,
                label = field.Label,
                kind = field.Kind.ToString(),
                typeId = type.Id,
                sensitivity = type.Sensitivity.ToString(),
                llmExposure = type.LlmExposure.ToString(),
                required = field.Required,
                supported = field.Supported,
            };
        }).ToArray(),
        fallback = state.Fields.Where(field => !field.Supported)
            .Select(field => $"{field.Label}: {FormFieldSupport.FallbackExplanation}").ToArray(),
    };

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static object Failure(Exception error) => new { isError = true, message = error.Message };
}

internal sealed record FormFieldArgument(
    [property: Description("Unique field key.")] string Name,
    [property: Description("Human label.")] string Label,
    [property: Description("Semantic type id, e.g. plain-text, date, secret.")] string Kind,
    [property: Description("Whether submit must include this field.")] bool Required = false,
    [property: Description("Allowed values for choice or multi-choice fields.")] IReadOnlyList<string>? Choices = null);
