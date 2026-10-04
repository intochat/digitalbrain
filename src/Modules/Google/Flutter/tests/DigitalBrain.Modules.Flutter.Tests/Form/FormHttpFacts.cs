using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Form;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Form;

[Collection(FlutterHostCollection.Name)]
public sealed class FormHttpFacts(FlutterHostFixture host)
{
    [Fact]
    public async Task GetReturnsTheSubmittedValues()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = host.Brain;
        var ws = host.Workspace();
        var form = brain.Get<IForm>(UiScope.Key(BrainScope.Create("owner", ws).Id, "intake"));
        var defined = await form.Define(new("Customer intake",
        [
            new("name", "Name", FieldKind.PlainText, true),
            new("birthDate", "Date of birth", FieldKind.Date, true),
        ]));
        await form.Submit(new([new("name", "Ada"), new("birthDate", "1815-12-10")], defined.Revision));

        var state = await brain.HttpClient.GetFromJsonAsync<FormState>($"/brains/{ws}/ui/forms/intake",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

        Assert.NotNull(state);
        Assert.True(state!.Submitted);
        Assert.Equal("Ada", state.Fields.Single(field => field.Name == "name").Value);
        Assert.Equal("1815-12-10", state.Fields.Single(field => field.Name == "birthDate").Value);
    }
}
