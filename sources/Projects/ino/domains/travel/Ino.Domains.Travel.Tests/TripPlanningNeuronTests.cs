using System.Text;
using Ino.Testing.E2E;
using Microsoft.Playwright;
using Xunit;

namespace Ino.Domains.Travel.Tests;

/// <summary>
/// End-to-end browser-driven tests for the trip-planning neuron.
///
/// Boots the real Aspire AppHost (with INO_TEST_MODE=true so silos use
/// BddMockChatClientFactory), opens Chromium against the kernel silo's HTTPS
/// URL, sends the user's prompt via the GoRouter <c>?q=</c> deep-link
/// (resolved by the root redirect to <c>/chat?q=…</c>; ChatScreen
/// auto-sends on first frame), and asserts on the gRPC-Web response payload.
/// Default mode is headed so the developer can watch the demo run; CI=true
/// (auto-set by every CI runner) flips to headless.
///
/// <strong>Why deep-link, not keyboard input:</strong> Flutter web with
/// CanvasKit only injects a real DOM <c>&lt;input&gt;</c> when accessibility
/// semantics is enabled AND the TextField gains focus. The auto-send route
/// goes through the same Bloc/gRPC path as a typed prompt without depending
/// on browser-level focus + a11y-mode plumbing.
///
/// <strong>Why gRPC interception, not DOM text:</strong> CanvasKit paints
/// card content into a shadow-DOM canvas — DOM <c>text=</c> selectors find
/// nothing. We intercept Chat() / RfwEvent() bodies and match on the RFW
/// library name + widget identifiers the gateway streams back.
///
/// Both scenarios go through <c>TripPlanner.ExecuteAsync</c>, which emits a
/// rich initial card (<c>ino.travel.intro</c>: WeatherSummaryCard + a list
/// of FlightCards). Subsequent hops (<c>flight.selected</c> →
/// <c>ino.travel.hotels</c>, etc.) are exercised by RichTripPlanningE2ETests
/// at the gRPC level — keeping this file scoped to the UI entry point keeps
/// browser test runtime down.
///
/// Run with:
///   dotnet test domains/travel/Ino.Domains.Travel.Tests
/// scoped to one neuron:
///   dotnet test ino.slnx --filter "NeuronDefinition=PlanTrip"
/// </summary>
[Collection(nameof(TripPlanningCollection))]
[Trait("Neuron", "PlanTrip")]
public class TripPlanningNeuronTests(InoBrowserFixture<Projects.Ino_AppHost> fx)
{
    const int TimeoutMs = 30_000;

    [Fact]
    public async Task plan_trip_to_tokyo_next_week_renders_intro_card()
    {
        var capture = new GrpcResponseCapture();
        fx.Page.Response += capture.OnResponse;

        try
        {
            await SendPromptAsync("plan a trip to Tokyo next week");

            await capture.WaitForIntroCardAsync(TimeoutMs);

            Assert.True(
                capture.ReceivedIntroCard,
                "Expected an ino.travel.intro RFW response carrying " +
                "WeatherSummaryCard + FlightCard widgets; got " +
                $"{capture.ChatResponseCount} chat frame(s) with content " +
                $"types: {string.Join(", ", capture.ContentTypesSeen)}");
        }
        finally
        {
            fx.Page.Response -= capture.OnResponse;
        }
    }

    [Fact]
    public async Task plan_trip_to_tokyo_without_dates_still_renders_intro_card()
    {
        var capture = new GrpcResponseCapture();
        fx.Page.Response += capture.OnResponse;

        try
        {
            await SendPromptAsync("plan a trip to Tokyo");

            // PlanTripPromptParser falls back to a default month when dates
            // are absent, so the dateless prompt lands on the same intro
            // card as the dated one — proving the new path doesn't bail
            // out via the deprecated AskClarification chip-row.
            await capture.WaitForIntroCardAsync(TimeoutMs);

            Assert.True(
                capture.ReceivedIntroCard,
                "Expected the dateless prompt to still emit ino.travel.intro " +
                "via TripPlanner.ExecuteAsync's month fallback; got " +
                $"{capture.ChatResponseCount} chat frame(s) with content " +
                $"types: {string.Join(", ", capture.ContentTypesSeen)}");
        }
        finally
        {
            fx.Page.Response -= capture.OnResponse;
        }
    }

    Task SendPromptAsync(string prompt) =>
        // GoRouter redirect: /?q=… → /chat?q=…; ChatScreen reads `q` and
        // dispatches the Bloc send on first build, which fires Chat()
        // through the gRPC-Web channel just like a keyboard submit would.
        fx.Page.GotoAsync($"{fx.KernelSiloUrl}?q={Uri.EscapeDataString(prompt)}");

    /// <summary>
    /// Tap-listener for gRPC-Web responses on the chat / fire endpoints.
    /// gRPC-Web frames are length-prefixed protobuf — we don't decode them,
    /// just match on the substring patterns the rich plan path emits:
    /// the <c>ino.travel.intro</c> content_type stamped by the gateway and
    /// the <c>WeatherSummaryCard</c> / <c>FlightCard</c> widget identifiers
    /// baked into the RFW description by <c>TripIntroBuilder</c>.
    /// </summary>
    sealed class GrpcResponseCapture
    {
        readonly List<string> _contentTypes = new();
        int _chatCount;
        bool _gotIntroCard;

        public int ChatResponseCount => _chatCount;
        public IReadOnlyList<string> ContentTypesSeen => _contentTypes;
        public bool ReceivedIntroCard => _gotIntroCard;

        public async void OnResponse(object? sender, IResponse response)
        {
            try
            {
                var url = response.Url;
                if (!url.Contains("/ino.v1.Ino/Chat") && !url.Contains("/ino.v1.Ino/RfwEvent"))
                    return;

                _chatCount++;
                var body = await response.BodyAsync();
                var text = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 16384));

                if (text.Contains("ino.travel.intro", StringComparison.Ordinal))
                {
                    _contentTypes.Add("ino.travel.intro");
                }

                // The intro card RFW description always imports the weather
                // and flights libraries; require both as a signal that the
                // payload is the hop-1 intro and not just a coincidental
                // mention of the content type.
                if (text.Contains("WeatherSummaryCard", StringComparison.Ordinal) &&
                    text.Contains("FlightCard", StringComparison.Ordinal))
                {
                    _gotIntroCard = true;
                }
            }
            catch
            {
                // Best-effort — gRPC streaming responses can be torn down
                // before BodyAsync resolves. The polling helper below
                // retries until the deadline.
            }
        }

        public async Task WaitForIntroCardAsync(int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (_gotIntroCard) return;
                await Task.Delay(200);
            }
        }
    }
}
