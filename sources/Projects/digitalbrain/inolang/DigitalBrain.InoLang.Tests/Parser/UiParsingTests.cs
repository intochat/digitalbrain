using DigitalBrain.InoLang.Ast;
using DigitalBrain.InoLang.Diagnostics;
using DigitalBrain.InoLang.Lexing;
using DigitalBrain.InoLang.Parsing;

namespace DigitalBrain.InoLang.Tests.Parsing;

public sealed class UiParsingTests
{
    static NeuronDoc Parse(string s)
    {
        var bag = new DiagnosticBag();
        var toks = new Lexer(s, bag).Lex();
        var doc = new Parser(toks, bag).ParseDocument();
        bag.HasErrors.Should().BeFalse(string.Join(";", bag.Items.Select(i => i.Message)));
        return doc!;
    }

    [Fact]
    public void Parses_ui_block_with_UiKit_constructor_nested_calls()
    {
        const string src = """
            neuron Acme.WeeklyDigest
              "Weekly digest neuron."
              using sheets = neuron(Google.Sheets)
              
              on Activated:
                log "activated"
                
              ui:
                UiKit.Column(
                  children: [
                    UiKit.Card(title: "Weekly Acme Digest", body: "Last run: never"),
                    UiKit.Button(label: "Run now", action: Activated)
                  ]
                )
                
            scenario "happy path":
              when Activated()
              then counter test == 1
            """;

        var d = Parse(src);
        d.Fqn.Should().Be("Acme.WeeklyDigest");
        d.Ui.Should().NotBeNull();
        d.Ui!.RootWidget.Should().NotBeNull();
        
        var root = d.Ui.RootWidget!;
        root.Name.Should().Be("Column");
        root.Children.Should().HaveCount(2);

        var card = root.Children[0];
        card.Name.Should().Be("Card");
        card.Arguments["title"].Should().Be("Weekly Acme Digest");
        card.Arguments["body"].Should().Be("Last run: never");

        var button = root.Children[1];
        button.Name.Should().Be("Button");
        button.Arguments["label"].Should().Be("Run now");
        button.Arguments["action"].Should().Be("Activated");

        // Verify JSON serialization
        var json = d.Ui.SerializeJson();
        json.Should().Contain("\"name\":\"Column\"");
        json.Should().Contain("\"name\":\"Card\"");
        json.Should().Contain("\"title\":\"Weekly Acme Digest\"");
        json.Should().Contain("\"name\":\"Button\"");
        json.Should().Contain("\"action\":\"Activated\"");
    }

    [Fact]
    public void Parses_ui_block_with_indented_child_block()
    {
        const string src = """
            neuron Acme.IndentedDigest
              "Indented digest neuron."
              
              on Activated:
                log "activated"
                
              ui:
                UiKit.Column:
                  UiKit.Card(title: "Indented", body: "Test")
                  UiKit.Text(content: "Child text")
                  
            scenario "happy path":
              when Activated()
              then counter test == 1
            """;

        var d = Parse(src);
        d.Fqn.Should().Be("Acme.IndentedDigest");
        d.Ui.Should().NotBeNull();
        d.Ui!.RootWidget.Should().NotBeNull();
        
        var root = d.Ui.RootWidget!;
        root.Name.Should().Be("Column");
        root.Children.Should().HaveCount(2);

        root.Children[0].Name.Should().Be("Card");
        root.Children[0].Arguments["title"].Should().Be("Indented");

        root.Children[1].Name.Should().Be("Text");
        root.Children[1].Arguments["content"].Should().Be("Child text");
    }

    [Fact]
    public void Parses_EarthGlobe_points_and_arcs_as_json_arrays()
    {
        const string src = """
            neuron DigitalBrain.WidgetCanvas.FlightNeuron
              "Globe with a route arc."

              on Activated:
                log "activated"

              ui:
                EarthGlobe(
                  autoRotate: true,
                  points: [{lat: 51.47, lng: -0.45}, {lat: 40.64, lng: -73.78}],
                  arcs: [{from: {lat: 51.47, lng: -0.45}, to: {lat: 40.64, lng: -73.78}, style: "dashed"}]
                )

            scenario "happy path":
              when Activated()
              then counter test == 1
            """;

        var d = Parse(src);
        var globe = d.Ui!.RootWidget!;
        globe.Name.Should().Be("EarthGlobe");
        globe.Arguments["autoRotate"].Should().Be("true");

        // points/arcs are captured as JSON literals, not flattened to scalars.
        globe.Arguments["points"].Should().Be("[{\"lat\":51.47,\"lng\":-0.45},{\"lat\":40.64,\"lng\":-73.78}]");
        globe.Arguments["arcs"].Should()
            .Be("[{\"from\":{\"lat\":51.47,\"lng\":-0.45},\"to\":{\"lat\":40.64,\"lng\":-73.78},\"style\":\"dashed\"}]");

        // SerializeJson emits them raw (real arrays), so jsonDecode on the client yields List/Map.
        var json = d.Ui.SerializeJson();
        json.Should().Contain("\"points\":[{\"lat\":51.47,\"lng\":-0.45}");
        json.Should().Contain("\"arcs\":[{\"from\":{\"lat\":51.47,\"lng\":-0.45}");
        json.Should().Contain("\"style\":\"dashed\"");
        // The arrays must not be wrapped/escaped as a quoted string.
        json.Should().NotContain("\"points\":\"[");
        json.Should().NotContain("\\\"lat\\\"");
    }

    [Fact]
    public void Serializes_scalar_string_starting_with_bracket_as_a_quoted_string()
    {
        // A scalar arg whose value merely starts with '[' must stay a quoted JSON
        // string — only captured data literals (tracked via RawJsonArgs) emit raw.
        const string src = """
            neuron Acme.Badge
              "Badge neuron."

              on Activated:
                log "activated"

              ui:
                Text(content: "[LIVE]")

            scenario "happy path":
              when Activated()
              then counter test == 1
            """;

        var d = Parse(src);
        var text = d.Ui!.RootWidget!;
        text.Arguments["content"].Should().Be("[LIVE]");
        text.RawJsonArgs.Should().NotContain("content");

        var json = d.Ui.SerializeJson();
        json.Should().Contain("\"content\":\"[LIVE]\"");
        json.Should().NotContain("\"content\":[");
    }
}
