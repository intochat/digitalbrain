using System.Text;
using System.Text.Json;
using Ino.Domains.Travel.FlightSearch.Rfw;
using Ino.Domains.Travel.Rfw;
using Xunit;

namespace Ino.Domains.Travel.Tests;

/// <summary>
/// Locks the DSL emitted by <see cref="FlightCardListBuilder"/>: the library
/// name, that the DSL contains a <c>FlightCard</c> column with the canonical
/// <c>event 'flight.selected' { … }</c> binding, and that the data JSON's
/// <c>items[i]</c> field names line up with the existing Flutter
/// <c>FlightCard</c> widget's <c>source.v(['airline'])</c> /
/// <c>source.v(['from'])</c> / etc. consumers.
/// </summary>
public sealed class FlightCardListBuilderTests
{
    [Fact]
    public void Builds_well_formed_DSL_for_three_flights()
    {
        var flights = new[]
        {
            new FlightOption("F1", "AirX", "LHR", "DPS", 800m, 900),
            new FlightOption("F2", "AirY", "LHR", "DPS", 700m, 1000),
            new FlightOption("F3", "AirZ", "LHR", "DPS", 900m, 800),
        };

        var rfw = FlightCardListBuilder.Build(flights);

        Assert.Equal("ino.travel.flights", rfw.LibraryName);
        var dsl = Encoding.UTF8.GetString(rfw.DescriptionDsl);
        Assert.Contains("widget root", dsl);
        Assert.Contains("FlightCard(", dsl);
        Assert.Contains("event 'flight.selected'", dsl);
        Assert.Contains("import ino.flights;", dsl);
        Assert.Contains("import core.widgets;", dsl);
    }

    [Fact]
    public void Data_JSON_field_names_match_Flutter_FlightCard_consumers()
    {
        var rfw = FlightCardListBuilder.Build(new[]
        {
            new FlightOption("F1", "AirX", "LHR", "DPS", 800m, 945),
        });

        using var doc = JsonDocument.Parse(rfw.DataPayload);
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToArray();

        Assert.Single(items);
        Assert.Equal("F1", items[0].GetProperty("flightId").GetString());
        Assert.Equal("AirX", items[0].GetProperty("airline").GetString());
        Assert.Equal("LHR", items[0].GetProperty("from").GetString());
        Assert.Equal("DPS", items[0].GetProperty("to").GetString());
        // price is rendered as int to match the Flutter widget's source.v<int>(['price']).
        Assert.Equal(800, items[0].GetProperty("price").GetInt32());
        // duration is the formatted "Xh Ym" string the widget paints — not the
        // raw minute count, which is internal to the server-side mock corpus.
        Assert.Equal("15h 45m", items[0].GetProperty("duration").GetString());
    }

    [Fact]
    public void Empty_flight_list_still_emits_parseable_DSL_and_empty_data_array()
    {
        var rfw = FlightCardListBuilder.Build(Array.Empty<FlightOption>());

        Assert.Equal("ino.travel.flights", rfw.LibraryName);
        var dsl = Encoding.UTF8.GetString(rfw.DescriptionDsl);
        Assert.Contains("widget root", dsl);

        using var doc = JsonDocument.Parse(rfw.DataPayload);
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }
}
