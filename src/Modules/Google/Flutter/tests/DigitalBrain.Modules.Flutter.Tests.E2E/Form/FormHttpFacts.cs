using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Contracts.Types;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Form;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.E2E.Form;

[Collection(FlutterBackendCollection.Name)]
public sealed class FormHttpFacts(FlutterBackendFixture host)
{
    [Fact]
    public async Task GetReturnsTheSubmittedValues()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await host.LeaseAsync(ct);
        var form = brain.Get<IForm>(UiScope.Key(BrainScope.Create("owner", brain.WorkspaceId).Id, "intake"));
        var defined = await form.Define(new("Customer intake",
        [
            new("name", "Name", FieldKind.PlainText, true),
            new("birthDate", "Date of birth", FieldKind.Date, true),
        ]));
        await form.Submit(new([new("name", "Ada"), new("birthDate", "1815-12-10")], defined.Revision));

        var state = await brain.HttpClient.GetFromJsonAsync<FormState>($"/brains/{brain.WorkspaceId}/ui/forms/intake",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

        Assert.NotNull(state);
        Assert.True(state!.Submitted);
        Assert.Equal("Ada", state.Fields.Single(field => field.Name == "name").Value);
        Assert.Equal("1815-12-10", state.Fields.Single(field => field.Name == "birthDate").Value);
    }
}
