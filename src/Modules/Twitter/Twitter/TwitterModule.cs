using System.Text.Json.Schema;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Twitter;

/// <summary>Registers the typed receipt source; no live X provider is configured.</summary>
public sealed class TwitterModule : Core.IModule
{
    public const string PostedSignal = "Posted";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // This is the emitted receipt contract, rather than the looser deserialization
        // contract: Accept validates every string and never announces a null Posted.
        var output = TwitterJson.Default.Posted.GetJsonSchemaAsNode().AsObject();
        output["type"] = "object";
        output["required"] = new JsonArray("eventId", "author", "text");
        output["additionalProperties"] = false;
        foreach (var property in output["properties"]!.AsObject())
        {
            property.Value!.AsObject()["type"] = "string";
        }
        builder.Services.AddSingleton(new BehaviorSourceContract("twitter",
            [new(PostedSignal, output.ToJsonString())]));
    }
}
