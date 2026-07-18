using System.Text;
using FluentAssertions;
using Ino.NeuronTesting;
using Xunit;

namespace Ino.NeuronTesting.Tests;

public sealed class RfwSnapshotTests
{
    [Fact]
    public void ContainsWidgets_returns_true_when_all_named_widgets_appear_in_description()
    {
        var dsl = """
            import ino.weather;
            import ino.flights;
            widget root = Column(children: [
              WeatherSummaryCard(season: data.season),
              FlightCard(airline: data.flights.0.airline),
            ]);
            """;
        var payload = RfwSnapshot.FromBytes(
            Encoding.UTF8.GetBytes(dsl),
            Encoding.UTF8.GetBytes("""{"season":"dry","flights":[{"airline":"ANA"}]}"""));

        payload.ContainsWidgets("WeatherSummaryCard", "FlightCard").Should().BeTrue();
        payload.ContainsWidgets("HotelCard").Should().BeFalse();
    }

    [Fact]
    public void DataAt_returns_value_at_simple_dotted_path()
    {
        var payload = RfwSnapshot.FromBytes(
            Encoding.UTF8.GetBytes("widget root = Container();"),
            Encoding.UTF8.GetBytes("""{"flights":[{"airline":"ANA","price":1180}]}"""));

        payload.DataAt<string>("flights.0.airline").Should().Be("ANA");
        payload.DataAt<int>("flights.0.price").Should().Be(1180);
        payload.DataAt<string>("flights.0.missing").Should().BeNull();
    }
}
