using System.Globalization;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Core;
using DigitalBrain.Flutter.Form.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Form;

[GrainType(UIVocabulary.FormType)]
internal sealed class FormNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<FormState> store)
    : Neuron<FormState>(store), IForm
{
    public Task<FormState> Define(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Title);
        if (definition.Title.Length > 200) { throw new ArgumentException("A form title is limited to 200 characters.", nameof(definition)); }
        if (definition.Fields.Count is 0 or > 64) { throw new ArgumentException("A form declares 1–64 fields.", nameof(definition)); }
        var names = new HashSet<string>(StringComparer.Ordinal);
        var fields = new List<FormFieldState>(definition.Fields.Count);
        foreach (var field in definition.Fields)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(field.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(field.Label);
            if (field.Name.Length > 64) { throw new ArgumentException("A field name is limited to 64 characters.", nameof(definition)); }
            if (field.Label.Length > 120) { throw new ArgumentException("A field label is limited to 120 characters.", nameof(definition)); }
            if (!names.Add(field.Name)) { throw new ArgumentException($"Field '{field.Name}' is declared twice.", nameof(definition)); }
            fields.Add(new FormFieldState
            {
                Name = field.Name,
                Label = field.Label,
                Kind = field.Kind,
                Required = field.Required,
                Choices = [.. field.Choices ?? []],
                Supported = FormFieldSupport.Supports(field.Kind),
            });
        }
        var state = Snapshot;
        state.FormId = this.GetPrimaryKeyString();
        state.Title = definition.Title.Trim();
        state.Fields = fields;
        state.Submitted = false;
        state.Revision++;
        return SaveAndReturn();
    }

    public Task<FormState> SetDraft(string fieldName, string value)
    {
        var field = Field(fieldName);
        ArgumentNullException.ThrowIfNull(value);
        if (field.Kind == FieldKind.Secret) { throw new ArgumentException("A secret field only accepts a SecretRef; raw secret values are never accepted.", nameof(value)); }
        field.Value = value;
        Snapshot.Revision++;
        return SaveAndReturn();
    }

    public Task<FormState> SetSecret(string fieldName, SecretRef reference)
    {
        var field = Field(fieldName);
        if (field.Kind != FieldKind.Secret) { throw new ArgumentException($"Field '{fieldName}' is not a secret field.", nameof(fieldName)); }
        _ = TypeCatalog.Validate(reference, FieldKind.Secret);
        field.SecretSet = true;
        field.Secret = reference;
        field.Value = null;
        Snapshot.Revision++;
        return SaveAndReturn();
    }

    // All values are validated before any field is touched, then written in one state write.
    public Task<FormState> Submit(FormSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (submission.ExpectedRevision != 0 && submission.ExpectedRevision != Snapshot.Revision)
        { throw new InvalidOperationException($"Expected form revision {submission.ExpectedRevision}; current revision is {Snapshot.Revision}."); }
        var provided = new Dictionary<string, FormFieldValue>(StringComparer.Ordinal);
        foreach (var value in submission.Values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value.Name);
            if (!provided.TryAdd(value.Name, value)) { throw new ArgumentException($"Field '{value.Name}' is submitted twice.", nameof(submission)); }
        }
        foreach (var name in provided.Keys)
        {
            if (!Snapshot.Fields.Any(field => field.Name == name)) { throw new ArgumentException($"The form has no field '{name}'.", nameof(submission)); }
        }
        var normalized = new List<(FormFieldState Field, string? Value, bool SecretSet, SecretRef? Secret)>(Snapshot.Fields.Count);
        foreach (var field in Snapshot.Fields)
        {
            provided.TryGetValue(field.Name, out var submitted);
            if (field.Kind == FieldKind.Secret)
            {
                if (submitted?.Secret is null)
                {
                    if (submitted?.Value is not null) { throw new TypeValidationException(field.Kind, submitted.Value, TypeCatalog.AllowedFor(field.Kind)); }
                    if (field.Required && !field.SecretSet) { throw new TypeValidationException(field.Kind, null, TypeCatalog.AllowedFor(field.Kind)); }
                    continue;
                }
                _ = TypeCatalog.Validate(submitted.Secret, FieldKind.Secret);
                normalized.Add((field, null, true, submitted.Secret));
                continue;
            }
            if (string.IsNullOrEmpty(submitted?.Value))
            {
                if (field.Required) { throw new TypeValidationException(field.Kind, submitted?.Value, TypeCatalog.AllowedFor(field.Kind)); }
                continue;
            }
            var text = submitted!.Value!;
            if (field.Kind == FieldKind.Choice && field.Choices.Count > 0 && !field.Choices.Contains(text, StringComparer.Ordinal))
            { throw new TypeValidationException(field.Kind, text, field.Choices); }
            if (field.Kind == FieldKind.MultiChoice && field.Choices.Count > 0)
            {
                foreach (var choice in text.Split('\n'))
                {
                    if (!field.Choices.Contains(choice, StringComparer.Ordinal)) { throw new TypeValidationException(field.Kind, choice, field.Choices); }
                }
            }
            var input = field.Kind == FieldKind.MultiChoice ? text.Split('\n') : (object)text;
            normalized.Add((field, Normalize(field.Kind, TypeCatalog.Validate(input, field.Kind)), false, null));
        }
        foreach (var (field, value, secretSet, secret) in normalized)
        {
            field.Value = value;
            field.SecretSet = secretSet;
            if (secret is not null) { field.Secret = secret; }
        }
        Snapshot.Submitted = true;
        Snapshot.Revision++;
        return SaveAndReturn();
    }

    [ReadOnly, Alias("read")]
    public Task<FormState> Read() { Snapshot.FormId = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }

    private FormFieldState Field(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Snapshot.Fields.SingleOrDefault(field => field.Name == name)
            ?? throw new KeyNotFoundException($"The form has no field '{name}'.");
    }

    private async Task<FormState> SaveAndReturn()
    {
        var state = Snapshot;
        await Save(state, new FormChanged(this.GetPrimaryKeyString(), state.Revision));
        return state;
    }

    private static string Normalize(FieldKind kind, object validated) => kind switch
    {
        FieldKind.Date => ((DateOnly)validated).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        FieldKind.DateTime => ((DateTime)validated).ToString("O", CultureInfo.InvariantCulture),
        FieldKind.Number => ((decimal)validated).ToString(CultureInfo.InvariantCulture),
        FieldKind.Boolean => (bool)validated ? "true" : "false",
        FieldKind.MultiChoice => string.Join('\n', (string[])validated),
        _ => (string)validated,
    };
}
