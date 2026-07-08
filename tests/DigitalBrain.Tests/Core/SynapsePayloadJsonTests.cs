using System.Text.Json;
using DigitalBrain.Core;

namespace DigitalBrain.Tests.Core;

public class SynapsePayloadJsonTests
{
    [Fact]
    public void Deserialize_Never_Produces_JsonElement_For_Nested_Payloads()
    {
        var result = JsonSerializer.Deserialize<Dictionary<string, object?>>("""
            { "flag": true, "nested": { "name": "demo", "count": 2 }, "items": [1, { "flag": true }], "missing": null }
            """, SynapsePayloadJson.Options)!;

        Assert.Equal(true, result["flag"]);

        var nested = Assert.IsType<Dictionary<string, object?>>(result["nested"]);
        Assert.Equal("demo", nested["name"]);
        Assert.Equal(2L, nested["count"]);

        var items = Assert.IsType<object?[]>(result["items"]);
        Assert.Equal(1L, items[0]);
        var item = Assert.IsType<Dictionary<string, object?>>(items[1]);
        Assert.Equal(true, item["flag"]);

        Assert.Null(result["missing"]);

        Assert.DoesNotContain(new[] { result["flag"], result["nested"], result["items"], result["missing"] },
            value => value is JsonElement);
    }

    [Fact]
    public void Deserialize_Uses_Double_For_Non_Integral_Numbers()
    {
        var result = JsonSerializer.Deserialize<Dictionary<string, object?>>("""{ "price": 2.5 }""", SynapsePayloadJson.Options)!;

        Assert.Equal(2.5, result["price"]);
    }
}
