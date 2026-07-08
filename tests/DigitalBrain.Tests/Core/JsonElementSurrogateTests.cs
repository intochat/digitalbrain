using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace DigitalBrain.Tests.Core;

public class JsonElementSurrogateTests
{
    [Fact]
    public void JsonElement_Round_Trips_Through_Orleans_Serialization()
    {
        var services = new ServiceCollection();
        services.AddSerializer();
        using var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<Serializer<JsonElement>>();

        using var document = JsonDocument.Parse("""{ "nested": { "count": 2 }, "items": [1, 2, 3] }""");
        var original = document.RootElement;

        var bytes = serializer.SerializeToArray(original);
        var roundTripped = serializer.Deserialize(bytes);

        Assert.Equal(original.GetRawText(), roundTripped.GetRawText());
    }
}
