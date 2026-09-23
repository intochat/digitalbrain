using DigitalBrain.Contracts.Types;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Form;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Form;

public sealed class FormFacts
{
    private static readonly FormDefinition Intake = new("Customer intake",
    [
        new("name", "Name", FieldKind.PlainText, Required: true),
        new("surname", "Surname", FieldKind.PlainText, Required: true),
        new("birthDate", "Date of birth", FieldKind.Date, Required: true),
    ]);

    [Fact]
    public async Task DefineAndMultiFieldSubmitLandInOneWriteEach()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var form = brain.Get<IForm>("owner/a/apps/forms/intake");
        var defined = await form.Define(Intake);
        Assert.Equal(1, defined.Revision);
        Assert.False(defined.Submitted);

        var submitted = await form.Submit(new(
        [
            new("name", "Ada"),
            new("surname", "Lovelace"),
            new("birthDate", "1815-12-10"),
        ], defined.Revision));

        // Three fields, one revision: the submit is a single atomic state write.
        Assert.Equal(2, submitted.Revision);
        Assert.True(submitted.Submitted);
        Assert.Equal("Ada", submitted.Fields.Single(f => f.Name == "name").Value);
        Assert.Equal("1815-12-10", submitted.Fields.Single(f => f.Name == "birthDate").Value);
    }

    [Fact]
    public async Task InvalidDateNamesTheAllowedValues()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var form = brain.Get<IForm>("owner/a/apps/forms/intake");
        await form.Define(Intake);
        var error = await Assert.ThrowsAsync<TypeValidationException>(() => form.Submit(new(
        [
            new("name", "Ada"),
            new("surname", "Lovelace"),
            new("birthDate", "date"),
        ])));
        Assert.Equal(FieldKind.Date, error.Kind);
        Assert.Contains("yyyy-MM-dd", error.Message);
    }

    [Fact]
    public async Task ChoiceRejectsUndeclaredValueNamingTheChoices()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var form = brain.Get<IForm>("owner/a/apps/forms/plan");
        await form.Define(new("Plan", [new("tier", "Tier", FieldKind.Choice, true, ["free", "pro"])]));
        var error = await Assert.ThrowsAsync<TypeValidationException>(() => form.Submit(new([new("tier", "gold")], 1)));
        Assert.Contains("free", error.Message);
        Assert.Contains("pro", error.Message);
    }

    [Fact]
    public async Task SecretFieldHoldsOnlyAReferenceAndRendersMasked()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var form = brain.Get<IForm>("owner/a/apps/forms/login");
        await form.Define(new("Login", [new("password", "Password", FieldKind.Secret)]));
        var secret = SecretRef.For("owner", "login-password", "Password", isSet: true);
        await form.SetSecret("password", secret);
        var state = await form.Read();
        var field = state.Fields.Single();
        Assert.True(field.SecretSet);
        Assert.Null(field.Value);
        // Raw secret values are never accepted through a draft.
        await Assert.ThrowsAsync<ArgumentException>(() => form.SetDraft("password", "hunter2"));
        // A raw value submitted for a secret field is rejected too; only a SecretRef is allowed.
        var error = await Assert.ThrowsAsync<TypeValidationException>(() => form.Submit(new([new("password", "hunter2")], state.Revision)));
        Assert.DoesNotContain("hunter2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReferenceKindDeclaresTheFallback()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var form = brain.Get<IForm>("owner/a/apps/forms/ref");
        var state = await form.Define(new("Ref", [new("customer", "Customer", FieldKind.Reference)]));
        var field = state.Fields.Single();
        Assert.False(field.Supported);
        Assert.Equal(FieldKind.Reference, field.Kind);
    }

    [Fact]
    public async Task SubmittedValuesSurviveReactivationInOneGrain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var form = brain.Get<IForm>("owner/a/apps/forms/intake");
        var defined = await form.Define(Intake);
        await form.Submit(new([new("name", "Ada"), new("surname", "Lovelace"), new("birthDate", "1815-12-10")], defined.Revision));
        await brain.DeactivateAsync(form, ct);
        var reloaded = await form.Read();
        Assert.True(reloaded.Submitted);
        Assert.Equal(2, reloaded.Revision);
        Assert.Equal(3, reloaded.Fields.Count);
    }
}
