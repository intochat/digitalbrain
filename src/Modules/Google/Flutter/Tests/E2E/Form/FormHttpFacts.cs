using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Form;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Form;

public sealed class FormHttpFacts
{
    [Fact]
    public async Task GetReturnsTheSubmittedValues()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create().WithModule<FlutterModule>(flutter => flutter.BackendOnly())
            .StartAsync(ct);
        var form = brain.Get<IForm>("intake");
        var defined = await form.Define(new("Customer intake",
        [
            new("name", "Name", FieldKind.PlainText, true),
            new("birthDate", "Date of birth", FieldKind.Date, true),
        ]));
        await form.Submit(new([new("name", "Ada"), new("birthDate", "1815-12-10")], defined.Revision));

        var state = await brain.HttpClient.GetFromJsonAsync<FormState>("/ui/forms/intake",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

        Assert.NotNull(state);
        Assert.True(state!.Submitted);
        Assert.Equal("Ada", state.Fields.Single(field => field.Name == "name").Value);
        Assert.Equal("1815-12-10", state.Fields.Single(field => field.Name == "birthDate").Value);
    }
}
